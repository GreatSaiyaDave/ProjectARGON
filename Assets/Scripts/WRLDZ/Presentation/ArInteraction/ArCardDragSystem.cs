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
    /// Arena artwork holograms are not tappable until real 3D models ship.
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

        /// <summary>Short tap on a floating disk phase chip (BATTLE / MAIN 2 / END).</summary>
        public Action<ArDiskPhaseKind> OnPhaseButtonTapped;

        /// <summary>Tap a public card on the opponent glance mat (face-up = full text).</summary>
        public Action<CardInstance, bool /*publicFace*/> OnOppGlanceCardTapped;

        /// <summary>Empty legal zone tap (disk pad or arena glass) while a hand card is selected.</summary>
        public Action<WRLDZ.Duel.Rules.RulesZoneKind, int> OnEmptyZoneTapped;

        /// <summary>When true, pointer-down prefers a legal zone over a hand card (zone pick).</summary>
        public bool HighlightsActive;

        /// <summary>True while a hand card is being dragged (free-look should not steal the pointer).</summary>
        public bool IsDragging => _drag != null;

        /// <summary>True while this system owns the pointer (armed tap or live drag).</summary>
        public bool OwnsPointer => _drag != null || _armedHand != null || _zoneTapArmed;

        bool _zoneTapArmed;
        WRLDZ.Duel.Rules.RulesZoneKind _zoneTapKind;
        int _zoneTapIndex;

        ArFloatingCard _drag;
        float _dragPlaneY;
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

        void Update()
        {
            if (StageCamera == null || Engine == null) return;
            PreferSet = WrldzInput.KeyHeld(KeyCode.LeftShift) || WrldzInput.KeyHeld(KeyCode.RightShift);
            if (_pendingCard != null)
                return;

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
        public void BeginFromScreen(Vector2 screen)
        {
            if (StageCamera == null || Engine == null) return;
            if (_drag != null || _pendingCard != null || _pointerDown) return;
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

        void StartHandDrag(ArFloatingCard card)
        {
            if (card == null || _drag != null || StageCamera == null) return;
            _armedHand = null;
            _drag = card;
            _drag.IsDragging = true;
            _dragPlane = new Plane(-StageCamera.transform.forward, card.transform.position);
            _drag.transform.position += StageCamera.transform.forward * -0.03f
                                       + StageCamera.transform.up * 0.02f;
            var lift = _drag.transform.localScale;
            _drag.transform.localScale = lift * 1.08f;
            PlayerDisk?.SetZonesHot(true);
            PlayerDisk?.PlayFx(DiskFxEvent.LegalZonePulse);
            ApplyLegalGlow(card.Card, null);
            EnsureHud();
            Hud?.ShowHint(StartHint(card.Card));
            WrldzAudio.PlayCardSlide();
            Debug.Log($"[WRLDZ AR] Drag start: {card.Card?.Name}");
        }

        public void MoveFromScreen(Vector2 screen)
        {
            if (_drag == null || StageCamera == null) return;
            if (!TryScreenToRay(screen, out var ray)) return;
            // Free roam on drag plane (no zone magnet until release)
            if (_dragPlane.Raycast(ray, out var enter))
            {
                var target = ray.GetPoint(enter);
                // Light smoothing so it doesn't feel glued-jerky
                _drag.transform.position = Vector3.Lerp(
                    _drag.transform.position, target, 1f - Mathf.Exp(-18f * Time.deltaTime));
            }

            // Soft hover highlight near legal zones (wider than commit radius)
            var zone = PlayerDisk?.FindNearestValid(
                _drag.transform.position, 0.42f, _drag.Card, Engine,
                Engine.Player, PreferSet);
            if (_hoverZone != zone)
            {
                _hoverZone?.SetHot(false);
                _hoverZone = zone;
                _hoverZone?.SetHot(true);
                ApplyLegalGlow(_drag.Card, zone);
            }

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
            if (_endFrame == Time.frameCount) return;
            _endFrame = Time.frameCount;

            if (_drag != null)
            {
                MoveFromScreen(screen);
                EndDrag();
                _armedHand = null;
                _pointerDown = false;
                return;
            }

            if (_armedHand != null)
            {
                var card = _armedHand.Card;
                _armedHand = null;
                _pointerDown = false;
                _zoneTapArmed = false;
                if (card != null)
                {
                    Debug.Log($"[WRLDZ AR] Hand tap: {card.Name}");
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

                    TryFieldTap(screen);
                }
            }

            _zoneTapArmed = false;
            _pointerDown = false;
        }

        bool TryPickLegalZone(Ray ray, out WRLDZ.Duel.Rules.RulesZoneKind kind, out int index)
        {
            kind = WRLDZ.Duel.Rules.RulesZoneKind.Monster;
            index = 0;
            var hits = Physics.RaycastAll(ray, 14f, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return false;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                // A closer hand card owns the click — don't steal it for zone pick
                if (h.collider.GetComponentInParent<ArFloatingCard>() != null)
                    return false;
                var pad = h.collider.GetComponentInParent<ArLegalZonePad>();
                if (pad != null && pad.OnDisk && pad.gameObject.activeInHierarchy)
                {
                    kind = pad.Kind;
                    index = pad.Index;
                    return true;
                }

                // Arena holos are presentation-only — never steal a disk zone pick.
                if (h.collider.GetComponentInParent<ArArenaCardVisual>() != null)
                    continue;

                var zone = h.collider.GetComponentInParent<ArDiskZone>();
                if (zone != null && zone.IsEmpty && zone.IsPlayerSide)
                {
                    kind = zone.Kind switch
                    {
                        ArDuelZoneKind.Monster => WRLDZ.Duel.Rules.RulesZoneKind.Monster,
                        ArDuelZoneKind.SpellTrap => WRLDZ.Duel.Rules.RulesZoneKind.SpellTrap,
                        ArDuelZoneKind.FieldSpell => WRLDZ.Duel.Rules.RulesZoneKind.FieldSpell,
                        ArDuelZoneKind.PendulumLeft => WRLDZ.Duel.Rules.RulesZoneKind.PendulumLeft,
                        ArDuelZoneKind.PendulumRight => WRLDZ.Duel.Rules.RulesZoneKind.PendulumRight,
                        _ => WRLDZ.Duel.Rules.RulesZoneKind.Monster
                    };
                    index = zone.Index;
                    return true;
                }
            }

            return false;
        }

        void TryFieldTap(Vector2 screen)
        {
            if (!TryScreenToRay(screen, out var ray)) return;

            // Only physical disk cards / GY are tappable for menus.
            // Arena holograms (artwork billboards) are presentation-only until real 3D models ship.
            var hits = Physics.RaycastAll(ray, 14f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            ArDiskCardVisual bestDisk = null;
            ArFloatingCard bestFloat = null;
            var bestCardScore = float.MaxValue;
            var frontCardDist = float.MaxValue;

            foreach (var h in hits)
            {
                if (h.collider == null) continue;

                // Explicitly ignore arena holograms (even if they still have pick colliders)
                if (h.collider.GetComponentInParent<ArArenaCardVisual>() != null)
                    continue;

                var gy = h.collider.GetComponentInParent<ArGraveyardHit>();
                if (gy != null && gy.gameObject.activeInHierarchy)
                {
                    var yours = gy.Disk != null ? gy.Disk.IsPlayerSide : gy.IsPlayerSide;
                    OnGraveyardTapped?.Invoke(yours);
                    Debug.Log("[WRLDZ AR] GY tap · you=" + yours);
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
                    if (glance.Glance.TryPick(ray, out var gc, out var face) && gc != null)
                    {
                        OnOppGlanceCardTapped?.Invoke(gc, face);
                        Debug.Log("[WRLDZ AR] Opp glance tap · " + gc.Name + " · face=" + face);
                        return;
                    }
                }

                if (OnFieldCardTapped == null) continue;

                var emptyPad = h.collider.GetComponentInParent<ArLegalZonePad>();
                if (emptyPad != null && emptyPad.OnDisk && emptyPad.gameObject.activeInHierarchy
                    && bestDisk == null && bestFloat == null)
                {
                    OnEmptyZoneTapped?.Invoke(emptyPad.Kind, emptyPad.Index);
                    Debug.Log($"[WRLDZ AR] Empty disk zone tap · {emptyPad.Kind}[{emptyPad.Index}]");
                    return;
                }

                var emptyDisk = h.collider.GetComponentInParent<ArDiskZone>();
                if (emptyDisk != null && emptyDisk.IsEmpty && emptyDisk.IsPlayerSide
                    && bestDisk == null && bestFloat == null)
                {
                    var rk = emptyDisk.Kind switch
                    {
                        ArDuelZoneKind.Monster => WRLDZ.Duel.Rules.RulesZoneKind.Monster,
                        ArDuelZoneKind.SpellTrap => WRLDZ.Duel.Rules.RulesZoneKind.SpellTrap,
                        ArDuelZoneKind.FieldSpell => WRLDZ.Duel.Rules.RulesZoneKind.FieldSpell,
                        ArDuelZoneKind.PendulumLeft => WRLDZ.Duel.Rules.RulesZoneKind.PendulumLeft,
                        ArDuelZoneKind.PendulumRight => WRLDZ.Duel.Rules.RulesZoneKind.PendulumRight,
                        _ => WRLDZ.Duel.Rules.RulesZoneKind.Monster
                    };
                    OnEmptyZoneTapped?.Invoke(rk, emptyDisk.Index);
                    Debug.Log($"[WRLDZ AR] Empty disk zone tap · {emptyDisk.Kind}[{emptyDisk.Index}]");
                    return;
                }

                var diskCard = h.collider.GetComponentInParent<ArDiskCardVisual>();
                if (diskCard != null && diskCard.Card != null)
                {
                    if (h.distance > frontCardDist + 0.05f) continue;
                    if (h.distance < frontCardDist) frontCardDist = h.distance;
                    var score = LateralToRay(ray, diskCard.transform.position) + h.distance * 0.02f;
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
                }
            }

            if (bestDisk != null && bestDisk.Card != null)
            {
                WrldzAudio.PlayCardTap();
                var yours = ResolveIsPlayerSide(bestDisk.Card, null, bestDisk.transform);
                OnFieldCardTapped.Invoke(bestDisk.Card, yours, bestDisk.IsMonsterCard);
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
                OnFieldCardTapped.Invoke(bestFloat.Card, yours, isMon);
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

        void EndDrag()
        {
            if (_drag == null) return;
            var card = _drag;
            card.IsDragging = false;

            var zone = PlayerDisk?.FindNearestValid(
                card.transform.position, 0.42f, card.Card, Engine,
                Engine.Player, PreferSet);

            PlayerDisk?.SetZonesHot(false);
            _hoverZone?.SetHot(false);
            _hoverZone = null;
            _drag = null;

            if (zone == null)
            {
                zone = PlayerDisk?.FindNearestValid(
                    card.transform.position, 1.15f, card.Card, Engine,
                    Engine.Player, PreferSet);
            }

            if (zone == null)
            {
                Hud?.ShowHint("Not a legal zone — card returned to hand.");
                Engine.Log("AR snap: no legal zone under the card.");
                card.ReturnToHandRest();
                ClearLegalGlow();
                return;
            }

            var legal = ArSnapRules.QueryLegal(Engine, Engine.Player, card.Card, zone);
            if (!legal.Any)
            {
                Hud?.ShowHint(legal.Hint);
                Engine.Log($"AR snap illegal: {legal.BlockedReason}");
                card.ReturnToHandRest();
                return;
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
            if (legal.CanSummonAtk && legal.CanSetMonster)
            {
                ApplyLegalGlow(card.Card, zone);
                AskChoice(card, zone, title,
                    "Normal Summon (face-up Attack)", false,
                    "Set (face-down Defense)", true);
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
                AskChoice(card, zone, title, "Normal Summon (face-up Attack)", false, null, false);
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
                AskChoice(card, zone, title,
                    legal.CanSetMonster ? "Set (face-down Defense)" : "Set (face-down)",
                    true, null, false);
                return;
            }

            FinishCommit(card, zone, preferSet: false);
        }

        void AskChoice(ArFloatingCard card, ArDiskZone zone, string title,
            string a, bool aIsSet, string b, bool bIsSet)
        {
            _pendingCard = card;
            _pendingZone = zone;
            card.IsDragging = false;
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
            card?.ReturnToHandRest();
            ClearLegalGlow();
            Engine?.Log("AR play cancelled — card returned to hand.");
        }

        void FinishCommit(ArFloatingCard card, ArDiskZone zone, bool preferSet)
        {
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
            }
            else
            {
                Hud?.ShowHint(result.Reason ?? "Illegal play.");
                Debug.Log($"[WRLDZ AR] Snap rejected: {result.Reason}");
                Engine.Log($"AR snap illegal: {result.Reason}");
                card.ReturnToHandRest();
                ClearLegalGlow();
            }
        }

        void ApplyLegalGlow(CardInstance card, ArDiskZone hover)
        {
            var ix = GetComponent<ArDuelInteractionSystem>() ??
                     FindFirstObjectByType<ArDuelInteractionSystem>();
            ix?.ShowLegalPlacements(card, hover);
        }

        void ClearLegalGlow()
        {
            var ix = GetComponent<ArDuelInteractionSystem>() ??
                     FindFirstObjectByType<ArDuelInteractionSystem>();
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
