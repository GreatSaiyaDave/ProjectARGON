using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Stable interaction volume directly in front of the torso.
    /// Spawns hand cards as floating holograms. The player's volume carries an <see cref="ArFloatingHand"/>:
    /// a translucent right hand that holds the cards as a fan, pinch-draws from the disk and riffles on shuffle
    /// (FLOATING_HAND.md). With <see cref="ArFloatingHand.Enabled"/> off (or no hand FBX) the cards use the
    /// older free-floating fan (<see cref="ComputeFanSlot"/>). The opponent volume carries the same hand, tinted
    /// by their disk: it draws for them and GENERATES the gesture of every card they put on their disk (there is no
    /// hand data for an AI or a remote duelist), in step with the engine (<see cref="GestureInFlight"/>).
    /// <para>
    /// Placed in Update (order 600, after <see cref="ArFreeLookCamera"/> applies the look at 500: the volume faces the
    /// stage camera's forward) and again in LateUpdate for late tracker poses. The Update placement is what the
    /// hand's draw / place coroutines see, so a card at the deck well no longer jitters against a turning camera.
    /// <see cref="ArOpponentDuelist"/> (650) then IK-solves its arm onto the hand placed this frame.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(600)]
    public class ArHandVolume : MonoBehaviour
    {
        public IArTrackingSource Tracker;
        /// <summary>Player disk — draws originate from this deck well.</summary>
        public ArDuelDiskRig SourceDeck;
        /// <summary>Far-side AI / P2 hand — backs toward the local camera, not tappable.</summary>
        public bool IsOpponent;
        /// <summary>Cards fan from left hand (wrist at inner edge, fan opens away from body). Defaults to IsOpponent.</summary>
        public bool IsLeftHand = true;
        /// <summary>
        /// When set, the volume sits on this transform instead of the tracker's torso pose (the AI opponent duelist
        /// hangs its fan in front of its own chest; the simulated opponent torso is not where a body would stand).
        /// </summary>
        public Transform AnchorOverride;
        /// <summary>Local player sees faces; the opposite hand is public backs only.</summary>
        public bool RevealFaces = true;
        public int Layer = 28;
        public Color Tint = new(0.55f, 0.95f, 1f);
        public float FanWidth = 0.72f;
        public float CardDepth = 0.08f;
        /// <summary>Holo size in stage units — large enough for EditorSim / phone RT picks.</summary>
        public float CardScale = 0.22f;
        /// <summary>Air gap between card bodies, as a fraction of scaled width.</summary>
        public const float BodyGapFrac = 0.08f;

        /// <summary>Volume-local units per real metre (1 on real device; card-matched in EditorSim).</summary>
        public float MetreScale
        {
            get
            {
                var ls = transform.lossyScale.y;
                return 1f / Mathf.Max(0.01f, ls);
            }
        }

        public Transform VolumeRoot { get; private set; }
        readonly List<ArFloatingCard> _cards = new();
        readonly Dictionary<int, ArFloatingCard> _byInstance = new();

        public IReadOnlyList<ArFloatingCard> Cards => _cards;

        /// <summary>The translucent hand that holds the fan (both sides).</summary>
        public ArFloatingHand FloatingHand { get; private set; }

        /// <summary>True when the hand owns the layout (switch on, FBX loaded).</summary>
        public bool HandLive => FloatingHand != null && FloatingHand.IsLive;

        /// <summary>Longest a generated place may keep its zone waiting before the card is shown anyway.</summary>
        public const float GeneratedPlaceTimeout = 3f;
        /// <summary>Grip + carry + seat of a generated place (the AI waits this long).</summary>
        public const float GeneratedPlaceSeconds =
            ArFloatingHand.PlaceGripSeconds + ArFloatingHand.PlaceCarrySeconds + ArDiskMotion.PadSnap + 0.25f;

        /// <summary>A generated place touched its zone: the disk (and the arena) can present the card now.</summary>
        public System.Action GestureLanded;

        readonly Queue<(ArFloatingCard card, ArDiskZone zone, ArZoneOrientation orient, bool reveal)> _places = new();
        /// <summary>One-shot Spells the opponent's hand is (about to be) revealing, by instance.</summary>
        readonly HashSet<int> _revealing = new();

        /// <summary>The opponent's hand shows this activated card instead of setting it on a zone (queued or playing).</summary>
        public bool IsRevealing(int instanceId) => _revealing.Contains(instanceId);

        /// <summary>A generated place is queued or on its way (its zone is still waiting for the card).</summary>
        public bool GestureInFlight
        {
            get
            {
                if (_places.Count > 0) return true;
                if (SourceDeck == null) return false;
                foreach (var z in SourceDeck.Zones)
                    if (z != null && z.IncomingGesture) return true;
                return false;
            }
        }

        // Engine hand order from the last sync (0 = leftmost) — the fan slots and riffle read it.
        readonly Dictionary<ArFloatingCard, int> _slotIndex = new();
        readonly List<int> _lastOrder = new();
        bool _lastHandLive;

        /// <summary>Cards in the engine hand at the last <see cref="SyncHand"/>.</summary>
        public int SlotCount { get; private set; }

        /// <summary>Engine hand index of a floating card, or -1 when it is not (or no longer) in the hand.</summary>
        public int SlotIndexOf(ArFloatingCard card) =>
            card != null && _slotIndex.TryGetValue(card, out var i) ? i : -1;

        public static ArHandVolume Create(Transform parent, int layer, Color tint,
            bool opponent = false)
        {
            var go = new GameObject(opponent ? "OppHandVolume" : "HandVolume");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var hv = go.AddComponent<ArHandVolume>();
            hv.Layer = layer;
            hv.Tint = tint;
            hv.IsOpponent = opponent;
            hv.IsLeftHand = opponent;
            hv.RevealFaces = !opponent;
            hv.VolumeRoot = new GameObject("Cards").transform;
            hv.VolumeRoot.SetParent(go.transform, false);
#if UNITY_EDITOR
            if (opponent)
                Debug.Log($"[OppHandVolume] Created at {go.transform.position}, VolumeRoot parent={hv.VolumeRoot.parent.name}");
#endif
            // No volume guide cube — it read as a messy cyan box in the AR stage
            // Opponent hand volumes use the free-floating fan (no FloatingHand).
            // Their right hand physically reaches to the disk via ArOpponentDuelist.ReachDisk/SolveReach.
            if (!opponent)
            {
                try { hv.FloatingHand = ArFloatingHand.Attach(hv); }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[WRLDZ AR] Floating hand skipped: " + ex.Message);
                }
            }

            return hv;
        }

        void Update() => PlaceVolume();

        /// <summary>Torso / anchor pose → this transform. True when a pose was available.</summary>
        bool PlaceVolume()
        {
            if (AnchorOverride != null)
            {
                transform.SetPositionAndRotation(AnchorOverride.position, AnchorOverride.rotation);
#if UNITY_EDITOR
                if (IsOpponent)
                    Debug.Log($"[OppHandVolume] PlaceVolume at AnchorOverride: pos={AnchorOverride.position} fwd={AnchorOverride.forward}");
#endif
                return true;
            }

            if (Tracker == null) return false;
            Pose torso;
            var ok = IsOpponent
                ? Tracker.TryGetOpponentTorso(out torso)
                : Tracker.TryGetTorso(out torso);
            if (!ok) return false;
            // Volume in front of chest, slightly down toward the worn disk
            // (human ready stance: hand holos between eyes and left-arm plate)
            var pos = torso.position + torso.forward * 0.28f + torso.up * -0.12f;
            transform.SetPositionAndRotation(pos, torso.rotation);
            return true;
        }

        void LateUpdate()
        {
            var placed = PlaceVolume();

            var live = HandLive;
            if (live != _lastHandLive)
            {
                // A/B flip (ArFloatingHand.Enabled) mid-duel: move every idle card to the other layout
                _lastHandLive = live;
                foreach (var fc in _cards)
                {
                    if (fc == null || fc.IsLockedToZone || fc.IsDragging || fc.IsDrawArriving) continue;
                    var i = SlotIndexOf(fc);
                    if (i >= 0) LayoutCard(fc, i, SlotCount);
                }
            }

            // mixed reality: the fan rides the player's real right palm instead of floating at the torso
            if (FloatingHand != null && !IsOpponent)
            {
                if (Tracker is IArHandInput hands && hands.TryGetHand(false, out var right) && right.Tracked)
                    FloatingHand.SetTrackedGrip(right.Grip);
                else
                    FloatingHand.ClearTrackedGrip();
            }

            if (FloatingHand != null) FloatingHand.Tick(_cards);
            TickGeneratedPlaces();
            // The held fan stacks cards in parallel planes (never interpenetrating), and its lifted card
            // must not shove neighbours; the depenetration pass is for the free-floating layout only.
            if (!live && placed) SeparateOverlappingCards();
        }

        /// <summary>Last SyncHand held back new cards (Spell/Trap result gate) — caller re-syncs later.</summary>
        public bool ArrivalsDeferred { get; private set; }

        readonly List<CardInstance> _syncHand = new();

        public void SyncHand(DuelistState who, CardDatabase db)
        {
            if (who == null || db == null) return;
            // Spell/Trap result gate (Pot of Greed…): cards that ARRIVE in the hand are results and wait
            // until the activating card has been readable (ActivationBeat). Cards leaving still leave now.
            var holdArrivals = Application.isPlaying && ActivationBeat.AnyResultPending;
            ArrivalsDeferred = false;
            _syncHand.Clear();
            foreach (var c in who.Hand)
            {
                if (c == null) continue;
                if (holdArrivals && !_byInstance.ContainsKey(c.InstanceId))
                {
                    ArrivalsDeferred = true;
                    continue;
                }

                _syncHand.Add(c);
            }

            var live = new HashSet<int>();
            var arriveSlot = 0;
            var arrivedThisSync = 0;
            var order = new List<int>(_syncHand.Count);
            foreach (var c in _syncHand)
                order.Add(c.InstanceId);
            // the SHUFFLE flag belongs to the player's volume; the opponent volume never consumes it
            var shuffled = !IsOpponent && ArFloatingHand.ConsumeShufflePending();
            var riffle = HandLive && (shuffled || IsReorderOnly(order));
            // the fan closes from the pre-shuffle angles: keep the old index of every card for the riffle
            var riffleFrom = riffle ? new Dictionary<ArFloatingCard, int>(_slotIndex) : null;
            _lastOrder.Clear();
            _lastOrder.AddRange(order);
            SlotCount = order.Count;
            _slotIndex.Clear();
            var slot = 0;
            for (var i = 0; i < _syncHand.Count; i++)
            {
                var card = _syncHand[i];
                if (card == null) continue;
                var handSlot = slot++;
                live.Add(card.InstanceId);
                if (!_byInstance.TryGetValue(card.InstanceId, out var fc) || fc == null)
                {
                    fc = ArFloatingCard.Spawn(VolumeRoot, card, db, Layer, Tint);
                    fc.KeepFaceHidden = !RevealFaces;
                    _byInstance[card.InstanceId] = fc;
                    _cards.Add(fc);
                    _slotIndex[fc] = handSlot;
                    LayoutCard(fc, handSlot, SlotCount);
                        #if UNITY_EDITOR
                                                if (IsOpponent)
                                                {
                                                    var wp = fc.transform.position;
                                                    var lp = fc.transform.localPosition;
                                                    Debug.Log($"[OppHandVolume] SPAWNED {card.Name} slot={handSlot} local=({lp.x:0.000},{lp.y:0.000},{lp.z:0.000}) world=({wp.x:0.000},{wp.y:0.000},{wp.z:0.000}) vol={transform.position}");
                                                }
                        #endif
                    fc.RefreshArtFace(showFace: RevealFaces);
                    if (Application.isPlaying && gameObject.activeInHierarchy)
                    {
                        if (HandLive)
                            FloatingHand.QueueDraw(fc);   // pinch-draw: the hand sequences these
                        else
                            BeginArriveFromDeck(fc, arriveSlot++);
                        arrivedThisSync++;
                    }
                    continue;
                }

                fc.Card = card;
                _slotIndex[fc] = handSlot;

                // Don't fight free-roam drag; layout only updates rest pose (soft spring)
                if (fc.IsLockedToZone || fc.IsDragging || fc.IsDrawArriving) continue;
                LayoutCard(fc, handSlot, SlotCount);
            }

            if (riffle && arrivedThisSync == 0)
                FloatingHand.PlayRiffle(riffleFrom);

            // Remove hand tracking for cards that left the hand.
            // Zone-locked cards stay in the scene (parented to disk) — do not Destroy.
            var dead = new List<int>();
            foreach (var kv in _byInstance)
            {
                if (live.Contains(kv.Key)) continue;
                // the opponent put it on their disk: their hand carries it there instead of it vanishing
                if (kv.Value != null && !kv.Value.IsLockedToZone && !kv.Value.HeldByGesture &&
                    !TryQueueGeneratedPlace(who, kv.Value))
                    ArObjectUtil.Destroy(kv.Value.gameObject);
                dead.Add(kv.Key);
            }

            foreach (var id in dead)
            {
                _byInstance.Remove(id);
                _cards.RemoveAll(c => c == null || c.Card == null || c.Card.InstanceId == id);
            }

            // the AI waits for its own drawing hand too, so the engine never runs ahead of the gesture
            if (arrivedThisSync > 0) DuelPresentationPacer.HoldDraw(arrivedThisSync);
        }

        /// <summary>
        /// Opponent only: a card that left their hand for a monster zone or a face-down Spell/Trap slot is carried
        /// there by their hand; a one-shot Spell they activated is shown to you (<see cref="ArFloatingHand.PlayReveal"/>).
        /// Other face-up activations keep their own activation choreography.
        /// </summary>
        bool TryQueueGeneratedPlace(DuelistState who, ArFloatingCard fc)
        {
            if (!IsOpponent || !Application.isPlaying || SourceDeck == null || who == null) return false;
            if (!gameObject.activeInHierarchy) return false;
            var card = fc != null ? fc.Card : null;
            if (card == null) return false;

            // A one-shot Spell the AI activated from its hand: the hand shows it to you instead of setting it on a
            // zone (owner 2026-10-04). The engine has already activated it (often resolved it): its disk slot and the
            // arena wait for the reveal, and its beat counts from the end of the reveal.
            // (FaceUp is not required: a resolved one-shot is usually already face-down in the GY at this sync)
            if (ArSnapRules.RevealsFromHand(card) &&
                SpellActivationPresentation.TryGet(card.InstanceId, out var act) && !act.PlayerSide &&
                Time.unscaledTime - act.QueuedUnscaledTime < 3f)
            {
                Debug.Log($"[WRLDZ AR] Opponent reveals {card.Name} (S/T {act.ZoneIndex}) before it activates on the disk");
                var rz = act.ZoneIndex >= 0 && act.ZoneIndex < SourceDeck.SpellTrapZones.Length
                    ? SourceDeck.SpellTrapZones[act.ZoneIndex]
                    : null;
                var wait = ArFloatingHand.RevealSeconds + ArFloatingHand.RevealDissolveSeconds + _places.Count * GeneratedPlaceSeconds;
                if (rz != null) rz.IncomingGestureUntil = Time.unscaledTime + GeneratedPlaceTimeout + wait;
                fc.IsDragging = true;   // out of the fan: no idle spring, no layout
                _revealing.Add(card.InstanceId);
                _places.Enqueue((fc, rz, ArZoneOrientation.FaceUpAttack, true));
                DuelPresentationPacer.Hold(wait + 0.2f, "Opponent shows a card…");
                return true;
            }

            ArDiskZone zone = null;
            var orient = ArZoneOrientation.FaceDownSet;
            if (who.TryFindMonster(card, out var mi) && mi >= 0 && mi < SourceDeck.MonsterZones.Length)
            {
                zone = SourceDeck.MonsterZones[mi];
                orient = !card.FaceUp ? ArZoneOrientation.FaceDownSet
                    : card.Position == BattlePosition.Defense ? ArZoneOrientation.FaceUpDefense
                    : ArZoneOrientation.FaceUpAttack;
            }
            else if (!card.FaceUp && who.TryFindSpellTrap(card, out var si) && si >= 0 &&
                     si < SourceDeck.SpellTrapZones.Length)
            {
                zone = SourceDeck.SpellTrapZones[si];
            }

            if (zone == null || zone.IncomingGesture) return false;
            if (card.FaceUp)
            {
                // the art faces its owner in the fan; as the card turns onto the pad it faces up for everyone
                fc.KeepFaceHidden = false;
                fc.RefreshArtFace(showFace: true);
            }

            fc.IsDragging = true;   // out of the fan: no idle spring, no layout
            zone.IncomingGestureUntil = Time.unscaledTime + GeneratedPlaceTimeout + _places.Count * GeneratedPlaceSeconds;
            _places.Enqueue((fc, zone, orient, false));
            DuelPresentationPacer.Hold(GeneratedPlaceSeconds * _places.Count, "Opponent plays a card…");
            return true;
        }

        void TickGeneratedPlaces()
        {
            if (_places.Count == 0) return;
            var hand = FloatingHand;
            var (fc, zone, orient, reveal) = _places.Peek();
            if (fc == null || (zone == null && !reveal))
            {
                _places.Dequeue();
                if (zone != null) zone.IncomingGestureUntil = 0f;
                if (fc != null && fc.Card != null) _revealing.Remove(fc.Card.InstanceId);
                GestureLanded?.Invoke();
                return;
            }

            if (hand == null || !hand.IsLive)
            {
                // Opponent without floating hand: animate card to zone directly
                if (!_animatingPlace)
                {
                    _animatingPlace = true;
                    StartCoroutine(AnimatePlaceToZone(fc, zone, orient, reveal));
                }
                return;
            }

            if (hand.IsPlacing || hand.IsDrawing || hand.IsRiffling) return;
            if (reveal)
            {
                var id = fc.Card != null ? fc.Card.InstanceId : 0;
                var startedReveal = hand.PlayReveal(fc,
                    atReveal: () =>
                    {
                        // shown: the activation is presented from now (disk slot, arena hologram, results)
                        ActivationBeat.RestartClock(id);
                        if (zone != null) zone.IncomingGestureUntil = 0f;
                        GestureLanded?.Invoke();
                        return true;
                    },
                    done: _ => _revealing.Remove(id));
                if (startedReveal) _places.Dequeue();
                else AbandonPlace();
                return;
            }

            var started = hand.PlayPlace(fc, zone, orient,
                atContact: () =>
                {
                    zone.IncomingGestureUntil = 0f;
                    zone.SnapLock(fc, orient);
                    GestureLanded?.Invoke();
                    return true;
                });
            if (started) _places.Dequeue();
            else AbandonPlace();
        }

        bool _animatingPlace;

        IEnumerator AnimatePlaceToZone(ArFloatingCard fc, ArDiskZone zone, ArZoneOrientation orient, bool reveal)
        {
            if (fc == null) { _animatingPlace = false; yield break; }

            // Disable spring physics during animation
            fc.IsDragging = true;
            fc.HeldByHand = false;

            Vector3 startPos = fc.transform.position;
            Quaternion startRot = fc.transform.rotation;
            Vector3 targetPos = zone != null ? zone.transform.position : transform.position;
            Quaternion targetRot = zone != null ? zone.transform.rotation : Quaternion.identity;

            // For reveal, show card face-up at the zone
            if (reveal && fc.Card != null)
                fc.RefreshArtFace(showFace: true);

            float duration = GeneratedPlaceSeconds;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Smoothstep for natural motion
                t = t * t * (3f - 2f * t);

                fc.transform.position = Vector3.Lerp(startPos, targetPos, t);
                fc.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

                yield return null;
            }

            // Snap to final position
            fc.transform.position = targetPos;
            fc.transform.rotation = targetRot;

            if (zone != null)
            {
                zone.IncomingGestureUntil = 0f;
                zone.SnapLock(fc, orient);
            }

            if (fc.Card != null) _revealing.Remove(fc.Card.InstanceId);
            _places.Dequeue();
            GestureLanded?.Invoke();
            _animatingPlace = false;
        }

        /// <summary>The hand cannot carry it (hidden, busy for good): the card is shown on its zone at once.</summary>
        void AbandonPlace()
        {
            var (fc, zone, _, _) = _places.Dequeue();
            if (zone != null) zone.IncomingGestureUntil = 0f;
            if (fc != null && fc.Card != null)
            {
                _revealing.Remove(fc.Card.InstanceId);
                ActivationBeat.RestartClock(fc.Card.InstanceId);
            }
            if (fc != null) ArObjectUtil.Destroy(fc.gameObject);
            GestureLanded?.Invoke();
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (IsOpponent)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(transform.position, 0.08f);
                UnityEditor.Handles.Label(transform.position + Vector3.up * 0.1f, $"OppHandVolume (Cards: {_cards.Count})");
                foreach (var fc in _cards)
                {
                    if (fc != null && fc.transform != null)
                    {
                        Gizmos.color = fc.IsLockedToZone ? Color.red : Color.green;
                        Gizmos.DrawWireSphere(fc.transform.position, 0.03f);
                        UnityEditor.Handles.Label(fc.transform.position + Vector3.up * 0.05f, fc.Card?.Name ?? "?");
                    }
                }
            }
        }
#endif

        void OnDisable()
        {
            // hidden mid-gesture (retract / zone mode): every waiting card is shown on its zone
            var any = _places.Count > 0;
            while (_places.Count > 0)
            {
                var (fc, zone, _, _) = _places.Dequeue();
                if (zone != null) zone.IncomingGestureUntil = 0f;
                if (fc != null && !fc.IsLockedToZone) ArObjectUtil.Destroy(fc.gameObject);
            }

            if (SourceDeck != null)
                foreach (var z in SourceDeck.Zones)
                    if (z != null && z.IncomingGesture) { z.IncomingGestureUntil = 0f; any = true; }
            _revealing.Clear();
            if (any) GestureLanded?.Invoke();
        }

        void BeginArriveFromDeck(ArFloatingCard fc, int slot)
        {
            if (fc == null) return;
            var delay = slot * ArDiskMotion.DrawStagger;
            if (SourceDeck != null &&
                SourceDeck.TryGetDrawOriginWorldPose(out _, out _, out var worldSize))
            {
                // Unit floating card is ~0.82 wide; match the magazine card, then grow in flight.
                var startS = Vector3.one * Mathf.Clamp(worldSize.x / 0.82f, 0.028f, 0.18f);
                fc.PlayDrawFromDeck(SourceDeck, startS, delay);
                if (!IsOpponent)
                    StartCoroutine(PlayDrawSfx(delay));
                if (delay <= 0.001f)
                    SourceDeck.PulseDispenseTopCard();
                else
                    StartCoroutine(DelayedDispense(delay));
                return;
            }

            fc.PlayFadeInAtRest(delay);
            if (!IsOpponent)
                StartCoroutine(PlayDrawSfx(delay));
        }

        IEnumerator PlayDrawSfx(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            WrldzAudio.PlayCardDraw();
        }

        IEnumerator DelayedDispense(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            SourceDeck?.PulseDispenseTopCard();
        }

        void LayoutCard(ArFloatingCard fc, int index, int total)
        {
            Vector3 local;
            Quaternion rot;
            Vector3 scale;
            if (HandLive)
                FloatingHand.SlotFor(index, total, out local, out rot, out scale);
            else
                ComputeFanSlot(index, total, CardScale, MetreScale, out local, out rot, out scale, IsLeftHand);
            fc.HeldByHand = HandLive;
            fc.SetHandRest(local, rot, scale);
#if UNITY_EDITOR
            if (IsOpponent && !HandLive)
            {
                var worldPos = fc.transform.parent != null ? fc.transform.parent.TransformPoint(local) : local;
                Debug.Log($"[OppHandVolume] Card {fc.Card?.Name ?? "?"} idx={index}/{total} local=({local.x:0.000},{local.y:0.000},{local.z:0.000}) world=({worldPos.x:0.000},{worldPos.y:0.000},{worldPos.z:0.000}) volPos={transform.position} volFwd={transform.forward}");
            }
#endif
        }

        /// <summary>
        /// Same cards as the last sync, different order, nothing drawn or played: the hand was shuffled
        /// (in AR the 2D drag-reorder tray is hidden, so a permutation only comes from SHUFFLE).
        /// </summary>
        bool IsReorderOnly(List<int> order)
        {
            if (order.Count < 2 || order.Count != _lastOrder.Count) return false;
            var same = true;
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i] == _lastOrder[i]) continue;
                same = false;
                break;
            }

            if (same) return false;
            var a = new HashSet<int>(order);
            return a.SetEquals(_lastOrder);
        }

        /// <summary>
        /// Rest pose for one card in a physical fan. Centers are far enough apart that
        /// the PhysicalCard boxes do not occupy the same volume (yaw included).
        /// </summary>
        public static void ComputeFanSlot(int index, int total, float cardScale, float metreScale,
            out Vector3 localPos, out Quaternion localRot, out Vector3 localScale,
            bool isLeftHand = false)
        {
            total = Mathf.Max(1, total);
            index = Mathf.Clamp(index, 0, total - 1);
            // cardScale is in unit mesh scale; metreScale converts to volume-local metres
            var scale = cardScale * metreScale * Mathf.Lerp(1f, 0.86f, Mathf.InverseLerp(5f, 10f, total));
            var yawSpan = Mathf.Lerp(8f, 16f, Mathf.InverseLerp(1f, 7f, total));
            var t = total == 1 ? 0.5f : index / (float)(total - 1);
            var spacing = RestCenterSpacing(total, scale, yawSpan);
            float yaw;
            if (isLeftHand)
            {
                // Left hand: wrist at index 0 (inner edge), fan opens toward -X (opponent's left, away from body)
                // Volume faces player: forward=+Z (toward player), right=+X (opponent's right).
                // Opponent's left = -X. Cards fan toward -X and slightly +Z (toward fingertips/player).
                var x = -index * spacing;
                var z = index * RestDepthSpacing(scale, yawSpan);
                localPos = new Vector3(x, 0f, z);
                // Yaw: index 0 points forward (0), outer cards fan toward -X (negative yaw)
                yaw = -yawSpan * t;
            }
            else
            {
                // Right hand / centered: fan centered at origin
                var x = (index - (total - 1) * 0.5f) * spacing;
                var z = (index - (total - 1) * 0.5f) * RestDepthSpacing(scale, yawSpan);
                localPos = new Vector3(x, 0f, z);
                // Centered fan: yaw from +yawSpan to -yawSpan
                yaw = Mathf.Lerp(yawSpan, -yawSpan, t);
            }
            // Art normal is local +Z. Volume faces torso.forward; flip 180° so art faces the camera.
            // For opponent (isLeftHand), volume faces player: forward=+Z toward player.
            // Camera is behind player looking toward opponent = -volume forward.
            // Card +Z should face camera = -volume forward. +180 yaw achieves this.
            localRot = Quaternion.Euler(-8f, yaw + 180f, 0f);
            localScale = Vector3.one * scale;
        }

        public static float RestCenterSpacing(int total, float cardScale, float yawSpanDeg = 16f)
        {
            total = Mathf.Max(1, total);
            var yaw = total == 1 ? 0f : yawSpanDeg;
            return ProjectedWidth(cardScale, yaw) +
                   ArFloatingCard.BodyLocalSize.x * cardScale * BodyGapFrac;
        }

        public static float RestDepthSpacing(float cardScale, float yawSpanDeg = 16f)
        {
            var body = ArFloatingCard.BodyLocalSize;
            var yaw = yawSpanDeg * Mathf.Deg2Rad;
            var zFromYaw = body.x * Mathf.Abs(Mathf.Sin(yaw)) * cardScale;
            var zFromThick = body.z * cardScale * 2.2f;
            return Mathf.Max(zFromYaw, zFromThick) + 0.004f;
        }

        public static float ProjectedWidth(float cardScale, float yawDeg)
        {
            var body = ArFloatingCard.BodyLocalSize;
            var r = yawDeg * Mathf.Deg2Rad;
            return (body.x * Mathf.Abs(Mathf.Cos(r)) + body.z * Mathf.Abs(Mathf.Sin(r))) * cardScale;
        }

        /// <summary>
        /// True when two rest poses' body boxes would interpenetrate (no scene required).
        /// </summary>
        public static bool RestBodiesOverlap(int i, int j, int total, float cardScale, float metreScale = 1f, bool isLeftHand = false)
        {
            if (i == j) return false;
            ComputeFanSlot(i, total, cardScale, metreScale, out var pa, out var ra, out var sa, isLeftHand);
            ComputeFanSlot(j, total, cardScale, metreScale, out var pb, out var rb, out var sb, isLeftHand);
            var half = ArFloatingCard.BodyLocalSize * 0.5f;
            var ea = Vector3.Scale(half, sa);
            var eb = Vector3.Scale(half, sb);
            return ObbOverlap(pa, ra, ea, pb, rb, eb);
        }

        static bool ObbOverlap(Vector3 pa, Quaternion ra, Vector3 ea,
            Vector3 pb, Quaternion rb, Vector3 eb)
        {
            var axes = new[]
            {
                ra * Vector3.right, ra * Vector3.up, ra * Vector3.forward,
                rb * Vector3.right, rb * Vector3.up, rb * Vector3.forward
            };
            var d = pb - pa;
            for (var a = 0; a < axes.Length; a++)
            {
                var axis = axes[a];
                if (axis.sqrMagnitude < 1e-8f) continue;
                axis.Normalize();
                var raProj = Mathf.Abs(Vector3.Dot(ra * Vector3.right, axis)) * ea.x +
                             Mathf.Abs(Vector3.Dot(ra * Vector3.up, axis)) * ea.y +
                             Mathf.Abs(Vector3.Dot(ra * Vector3.forward, axis)) * ea.z;
                var rbProj = Mathf.Abs(Vector3.Dot(rb * Vector3.right, axis)) * eb.x +
                             Mathf.Abs(Vector3.Dot(rb * Vector3.up, axis)) * eb.y +
                             Mathf.Abs(Vector3.Dot(rb * Vector3.forward, axis)) * eb.z;
                if (Mathf.Abs(Vector3.Dot(d, axis)) > raProj + rbProj + 1e-4f)
                    return false;
            }

            return true;
        }

        void SeparateOverlappingCards()
        {
            var n = _cards.Count;
            if (n < 2) return;
            for (var i = 0; i < n; i++)
            {
                var a = _cards[i];
                if (a == null || a.IsLockedToZone || a.IsDrawArriving) continue;
                var colA = a.GetComponent<Collider>();
                if (colA == null || !colA.enabled) continue;
                for (var j = i + 1; j < n; j++)
                {
                    var b = _cards[j];
                    if (b == null || b.IsLockedToZone || b.IsDrawArriving) continue;
                    var colB = b.GetComponent<Collider>();
                    if (colB == null || !colB.enabled) continue;
                    if (!Physics.ComputePenetration(
                            colA, a.transform.position, a.transform.rotation,
                            colB, b.transform.position, b.transform.rotation,
                            out var dir, out var dist))
                        continue;
                    if (dist < 1e-5f) continue;
                    var push = dir * dist;
                    if (a.IsDragging && !b.IsDragging)
                        b.AddWorldSeparation(push);
                    else if (b.IsDragging && !a.IsDragging)
                        a.AddWorldSeparation(-push);
                    else
                    {
                        a.AddWorldSeparation(-push * 0.5f);
                        b.AddWorldSeparation(push * 0.5f);
                    }
                }
            }
        }

        public ArFloatingCard PickByRay(Ray ray, float maxDist = 3f)
        {
            if (IsOpponent || HandBusy) return null;
            ArFloatingCard best = null;
            var bestT = maxDist;
            foreach (var c in _cards)
            {
                if (c == null || c.IsLockedToZone || c.IsDrawArriving) continue;
                var col = c.GetComponent<Collider>();
                if (col == null) continue;
                if (col.Raycast(ray, out var hit, maxDist) && hit.distance < bestT)
                {
                    bestT = hit.distance;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>
        /// Click pick: among overlapping fan cards, the one whose center is
        /// closest on screen to the pointer. Falls back to a tight ray radius.
        /// </summary>
        public ArFloatingCard PickForPointer(Camera cam, Vector2 screen, Ray ray, float maxDist = 12f)
        {
            if (IsOpponent || HandBusy) return null;
            // Held fan: the hand picks (nearest card box along the ray, tested on the transforms of this frame —
            // physics auto-sync is off, so collider raycasts saw last step's poses — with hysteresis on the hovered
            // and selected cards so their own lift cannot move them out from under the pointer and flicker).
            if (HandLive) return FloatingHand.PickCard(ray) ?? PickNearestToRay(ray, 0.04f, maxDist);
            ArFloatingCard best = null;
            var bestScore = float.MaxValue;
            var anyHit = false;
            foreach (var c in _cards)
            {
                if (c == null || c.IsLockedToZone || c.IsDrawArriving) continue;
                var col = c.GetComponent<Collider>();
                if (col == null || !col.Raycast(ray, out var hit, maxDist)) continue;
                anyHit = true;
                // Prefer the card whose face-plane hit is nearest its own center
                // so overlapping fan colliders don't open a neighbor.
                var local = c.transform.InverseTransformPoint(hit.point);
                var planar = local.x * local.x + local.y * local.y;
                var score = planar * 8f + hit.distance * 0.05f;
                if (cam != null)
                {
                    var sp = (Vector2)cam.WorldToScreenPoint(c.transform.position);
                    score += (sp - screen).sqrMagnitude * 0.00002f;
                }

                if (score >= bestScore) continue;
                bestScore = score;
                best = c;
            }

            if (anyHit) return best;
            return PickNearestToRay(ray, 0.04f, maxDist);
        }

        /// <summary>
        /// Loose pick: closest card whose center is within <paramref name="maxRadius"/> of the ray.
        /// Used when strict collider hits fail (scale / camera noise).
        /// </summary>
        public ArFloatingCard PickNearestToRay(Ray ray, float maxRadius = 0.15f, float maxDist = 8f)
        {
            if (IsOpponent || HandBusy) return null;
            ArFloatingCard best = null;
            var bestScore = float.MaxValue;
            foreach (var c in _cards)
            {
                if (c == null || c.IsLockedToZone || c.IsDrawArriving) continue;
                var to = c.transform.position - ray.origin;
                var along = Vector3.Dot(to, ray.direction);
                if (along < 0.05f || along > maxDist) continue;
                var closest = ray.origin + ray.direction * along;
                var lateral = Vector3.Distance(closest, c.transform.position);
                if (lateral > maxRadius) continue;
                var score = lateral + along * 0.02f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = c;
            }

            return best;
        }

        /// <summary>The hand is riffling: cards are mid-flick, picks wait until the fan reopens.</summary>
        bool HandBusy => HandLive && FloatingHand.IsRiffling;

        /// <summary>The floating card for an engine hand card (null when it is not in this volume).</summary>
        public bool TryGetCard(int instanceId, out ArFloatingCard fc)
        {
            return _byInstance.TryGetValue(instanceId, out fc) && fc != null;
        }

        public void RemoveCard(ArFloatingCard fc)
        {
            if (fc?.Card == null) return;
            _byInstance.Remove(fc.Card.InstanceId);
            _cards.Remove(fc);
            _slotIndex.Remove(fc);
            if (FloatingHand != null && FloatingHand.Selected == fc) FloatingHand.ClearSelected();
        }
    }
}
