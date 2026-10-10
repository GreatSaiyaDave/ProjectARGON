using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Presentation bus for Spell/Trap field holograms.
    ///
    /// Yugipedia <c>Activate</c>: a Spell/Trap is played face-up from hand
    /// or <b>flipped from face-down on the field</b>.
    /// Anime Activate: the Set card pops off the pad, travels toward its
    /// controller, and leans so the face is readable. One-shots hover then
    /// fade. Continuous / Field / Equip settle back over the zone.
    ///
    /// Timing (owner report 2026-09-23: "Spells/traps do not seem to stay revealed long enough for
    /// players to realize what happened"): every activation opens an <see cref="ActivationBeat"/>.
    /// The rules engine still resolves synchronously; only the visuals of the results wait until the
    /// card's art has been readable for <see cref="ReadBeforeResultSeconds"/>, then the card lingers for
    /// <see cref="ReadAfterResultSeconds"/>. ALL timing constants for the beat live here.
    /// </summary>
    public static class SpellActivationPresentation
    {
        public struct Event
        {
            public int InstanceId;
            public int CardId;
            public string CardName;
            public bool PlayerSide;
            public int ZoneIndex; // 0–4 S/T, -1 field, -2 unknown
            public bool StaysOnField;
            /// <summary>Unused: lingering S/T stand upright. Kept so callers compile.</summary>
            public bool FlatOnBoard;
            public bool ResolveToGy;
            public int SpellCounters;
            public float QueuedUnscaledTime;
        }

        // ── Beat timing (seconds). Single source of truth — see ActivationBeat. ─────────────────
        //
        //  t0 engine activation ─┬─ disk: eject 0.55 → flip (art at 0.90) ───────────── hold ── fly to GY
        //                        └─ arena: wait 0.90 → rise 1.10 (art at 1.39) → present/hover → fade 0.75
        //  results (shatter / LP / draw / flips) are presented at  artReadable + ReadBeforeResult
        //  one-shot fades at  max(result + ReadAfterResult, artReadable + MinReadable)
        //  chain: link N resolves → its result beat → +ChainLinkResultGap → link N-1's result beat …

        /// <summary>How long the full activate rise+reveal should take (presentation).</summary>
        public const float ActivateSequenceSeconds = 1.65f;

        /// <summary>Arena rise duration (pop off the pad → present pose).</summary>
        public const float RiseSeconds = ActivateSequenceSeconds - 0.55f;

        /// <summary>Fraction of the rise at which the face art replaces the card back (0.32 pop + 18% of travel).</summary>
        public const float RiseArtRevealFraction = 0.32f + 0.68f * 0.18f;

        /// <summary>Arena waits for the disk toaster to turn the card past halfway before it starts.</summary>
        public const float ArenaStartDelaySeconds = ArDiskMotion.ArenaAfterSetFlip;

        /// <summary>Engine activation → arena art readable (≈ 1.39 s). Estimate until the visual reports the real reveal.</summary>
        public const float ArtReadableDelaySeconds = ArenaStartDelaySeconds + RiseSeconds * RiseArtRevealFraction;

        // Owner report 2026-09-28: still "unable to see the opponent or my card long enough to know what's about to
        // happen" (the opponent's cards had also been turned back-side toward the viewer). The effect now lands later
        // inside the same readable window, so the whole beat keeps its length (PlayerBeat / OpponentBeat unchanged).

        /// <summary>Art readable → first result presented (your own card).</summary>
        public const float ReadBeforeResultSeconds = 2.2f;

        /// <summary>Art readable → first result presented (opponent's card: you did not choose it, so read longer).</summary>
        public const float OpponentReadBeforeResultSeconds = 2.8f;

        /// <summary>Result presented → one-shot starts its fade (your own card).</summary>
        public const float ReadAfterResultSeconds = 2.3f;

        /// <summary>Result presented → one-shot starts its fade (opponent's card).</summary>
        public const float OpponentReadAfterResultSeconds = 3.0f;

        /// <summary>
        /// Minimum total art-readable window, player's own S/T (art readable → fade start).
        /// Historic name: this was the fixed arena hover. The disk toaster also holds the card up
        /// (ejected, face-up) until the same fade time.
        /// </summary>
        public const float MinHoverSeconds = 4.5f;

        /// <summary>Minimum total art-readable window, opponent's S/T.</summary>
        public const float OpponentReadHoverSeconds = 6.5f;

        /// <summary>Chains: gap between one link's result beat and the next (reverse order) link's.</summary>
        public const float ChainLinkResultGapSeconds = 1.5f;

        /// <summary>
        /// Chains built inside one engine call (AI auto-response): a later link's arena reveal starts
        /// this long after the previous link's art became readable, so links are revealed in order.
        /// </summary>
        public const float ChainRevealGapSeconds = 1.0f;

        /// <summary>Name banner switches to a resolving link this long before its result is shown…</summary>
        public const float BannerResultLeadSeconds = 1.0f;

        /// <summary>…and keeps it this long after (lead + tail = <see cref="ChainLinkResultGapSeconds"/>).</summary>
        public const float BannerResultTailSeconds = ChainLinkResultGapSeconds - BannerResultLeadSeconds;

        /// <summary>
        /// A chain link destroyed by a later link (MST on the Trap it answers) that still resolves:
        /// its own effect is shown with the card on screen, then it shatters this much later.
        /// </summary>
        public const float OwnResultBeforeShatterSeconds = 1.0f;

        /// <summary>Fade duration when leaving to GY after activation.</summary>
        public const float FadeToGySeconds = 0.75f;

        /// <summary>Lingering (Continuous / Equip / Swords) card glides from present pose back over its zone.</summary>
        public const float SettleSeconds = 0.45f;

        /// <summary>No beat may hold results, the AI or the player's phase advance longer than this (seconds from activation).</summary>
        public const float BeatHardCapSeconds = 20f;

        /// <summary>
        /// A card still waiting on the field for its resolution (chain window, target pick) keeps its
        /// present pose at most this long after activation, then settles over its zone until it resolves.
        /// </summary>
        public const float UnresolvedHoverCapSeconds = 12f;

        /// <summary>Disk toaster read pause when no beat exists (legacy / edit mode).</summary>
        public const float DiskOneShotMinReadSeconds = 1.2f;

        /// <summary>Whole player beat when nothing pushes it later: art + max(RBR+RAR, MinHover) + fade ≈ 6.64 s.</summary>
        public const float PlayerBeatSeconds = ArtReadableDelaySeconds +
                                               (ReadBeforeResultSeconds + ReadAfterResultSeconds > MinHoverSeconds
                                                   ? ReadBeforeResultSeconds + ReadAfterResultSeconds
                                                   : MinHoverSeconds) +
                                               FadeToGySeconds;

        /// <summary>Whole opponent beat when nothing pushes it later ≈ 8.64 s.</summary>
        public const float OpponentBeatSeconds = ArtReadableDelaySeconds +
                                                 (OpponentReadBeforeResultSeconds + OpponentReadAfterResultSeconds >
                                                  OpponentReadHoverSeconds
                                                     ? OpponentReadBeforeResultSeconds + OpponentReadAfterResultSeconds
                                                     : OpponentReadHoverSeconds) +
                                                 FadeToGySeconds;

        static readonly Dictionary<int, Event> _byInstance = new();
        static readonly List<Event> _ghostSpawnQueue = new();
        /// <summary>Activations the engine undid (target pick cancelled / timed out) — nothing resolved.</summary>
        static readonly Dictionary<int, float> _cancelled = new(); // instance → unscaled time of the undo

        public static void Clear()
        {
            _byInstance.Clear();
            _ghostSpawnQueue.Clear();
            _cancelled.Clear();
            ActivationBeat.Clear();
        }

        /// <summary>Called when a Spell/Trap is placed face-up for activation.</summary>
        public static void RegisterActivation(
            int instanceId,
            int cardId,
            string cardName,
            bool playerSide,
            int zoneIndex,
            bool staysOnField,
            bool flatOnBoard = false,
            bool isTrap = false)
        {
            if (instanceId <= 0) return;
            // An unresolved beat whose event is gone (cancelled / bounced) must not be reused.
            var hadEvent = _byInstance.ContainsKey(instanceId);
            _cancelled.Remove(instanceId);

            // Idempotent within one tick (GitHub lineage): one activation is often registered twice
            // in the same frame (PlaceFaceUp, then FinishSpellTrap once it knows the card stays).
            // Update the existing entry in place instead of replacing it with a fresh event —
            // replacing it (new QueuedUnscaledTime) makes the arena/disk replay the rise +
            // slot-insert, which is the "2 copies slotted into the disk" duplicate. A later
            // re-activation (another frame) still gets a fresh event and beat.
            if (_byInstance.TryGetValue(instanceId, out var existing) &&
                existing.QueuedUnscaledTime == Time.unscaledTime)
            {
                existing.CardId = cardId;
                if (!string.IsNullOrEmpty(cardName)) existing.CardName = cardName;
                existing.PlayerSide = playerSide;
                if (zoneIndex != -2) existing.ZoneIndex = zoneIndex;
                existing.StaysOnField = staysOnField || flatOnBoard || existing.StaysOnField;
                _byInstance[instanceId] = existing;
                ActivationBeat.Open(instanceId, cardId, cardName, playerSide, isTrap, hadEvent);
                return;
            }
            var e = new Event
            {
                InstanceId = instanceId,
                CardId = cardId,
                CardName = cardName ?? "",
                PlayerSide = playerSide,
                ZoneIndex = zoneIndex,
                StaysOnField = staysOnField || flatOnBoard,
                FlatOnBoard = false,
                ResolveToGy = false,
                SpellCounters = 0,
                QueuedUnscaledTime = Time.unscaledTime
            };
            _byInstance[instanceId] = e;
            ActivationBeat.Open(instanceId, cardId, cardName, playerSide, isTrap, hadEvent);
        }

        public static bool IsFlatOnBoard(int instanceId) =>
            _byInstance.TryGetValue(instanceId, out var e) && e.FlatOnBoard;

        /// <summary>
        /// Card finished resolving and is going to GY (or left the field).
        /// Presentation should fade the hologram rather than shatter.
        /// </summary>
        public static void QueueFadeToGy(int instanceId)
        {
            if (instanceId <= 0) return;
            if (_byInstance.TryGetValue(instanceId, out var e))
            {
                e.ResolveToGy = true;
                _byInstance[instanceId] = e;
            }
            else
            {
                // Never registered (edge path) — still request fade if a visual exists
                _byInstance[instanceId] = new Event
                {
                    InstanceId = instanceId,
                    ResolveToGy = true,
                    ZoneIndex = -2,
                    QueuedUnscaledTime = Time.unscaledTime
                };
            }

            ActivationBeat.MarkResolved(instanceId);
        }

        /// <summary>The engine finished resolving this activation (lingering cards stay on the field).</summary>
        public static void MarkResolved(int instanceId) => ActivationBeat.MarkResolved(instanceId);

        /// <summary>
        /// The engine UNDID this activation (target pick cancelled or timed out: back to hand / back to
        /// Set). Nothing resolved, so its beat closes at once: no banner, no result gate, no pacer or
        /// phase-advance hold, and a re-activation of the same card gets a fresh beat.
        /// </summary>
        public static void CancelActivationPresentation(int instanceId)
        {
            if (instanceId <= 0) return;
            _byInstance.Remove(instanceId);
            _ghostSpawnQueue.RemoveAll(g => g.InstanceId == instanceId);
            _cancelled[instanceId] = Time.unscaledTime;
            ActivationBeat.Cancel(instanceId);
        }

        /// <summary>
        /// The card left the field by a non-destroy move (bounce to hand). An activation that had not
        /// resolved yet is abandoned like a cancel — unless it is being moved as another card's result
        /// this frame (chain), whose beat keeps the timing.
        /// </summary>
        public static void AbandonActivation(int instanceId)
        {
            if (instanceId <= 0) return;
            _byInstance.Remove(instanceId);
            if (!ActivationBeat.IsUnresolvedAndIdle(instanceId)) return;
            _ghostSpawnQueue.RemoveAll(g => g.InstanceId == instanceId);
            _cancelled[instanceId] = Time.unscaledTime;
            ActivationBeat.Cancel(instanceId);
        }

        /// <summary>
        /// Presentation-only: the arena hologram shattered (destroyed by effect), drop the activation
        /// event so no fade / reseat follows. The beat keeps its clock (its results may still be due).
        /// </summary>
        public static void DropActivationEvent(int instanceId)
        {
            if (instanceId <= 0) return;
            _byInstance.Remove(instanceId);
        }

        /// <summary>
        /// True if the engine undid this activation recently (within the beat cap) and it has not been
        /// re-activated since — the disk then clears the card quietly instead of flying it to the GY.
        /// </summary>
        public static bool WasCancelled(int instanceId) =>
            _cancelled.TryGetValue(instanceId, out var at) && Time.unscaledTime - at < BeatHardCapSeconds;

        public static bool TryGet(int instanceId, out Event e) =>
            _byInstance.TryGetValue(instanceId, out e);

        public static bool HasActivation(int instanceId) =>
            _byInstance.ContainsKey(instanceId);

        /// <summary>Copy live activation events (disk ghost insert does not dequeue arena ghosts).</summary>
        public static void CollectActive(List<Event> dst)
        {
            if (dst == null) return;
            dst.Clear();
            foreach (var e in _byInstance.Values)
                dst.Add(e);
        }

        public static bool WantsFadeToGy(int instanceId) =>
            _byInstance.TryGetValue(instanceId, out var e) && e.ResolveToGy;

        /// <summary>Presentation consumed the activate sequence trigger (spawn / flip-up).</summary>
        public static bool TryConsumeActivateTrigger(int instanceId, out Event e)
        {
            e = default;
            if (!_byInstance.TryGetValue(instanceId, out e)) return false;
            // Keep entry for counters / fade; only mark that sequence was started via flag reuse
            return true;
        }

        public static void SetSpellCounters(int instanceId, int count)
        {
            if (!_byInstance.TryGetValue(instanceId, out var e)) return;
            e.SpellCounters = Mathf.Max(0, count);
            _byInstance[instanceId] = e;
        }

        public static int GetSpellCounters(int instanceId) =>
            _byInstance.TryGetValue(instanceId, out var e) ? e.SpellCounters : 0;

        /// <summary>
        /// When rules already sent the card to GY in the same frame as activation,
        /// no zone occupant remains — enqueue a ghost for the hologram manager.
        /// </summary>
        public static void EnqueueGhostIfNeeded(int instanceId)
        {
            if (!_byInstance.TryGetValue(instanceId, out var e)) return;
            if (!e.ResolveToGy) return;
            // Stub (never registered): no art, no side — a blank ghost would read as a bug.
            if (e.CardId == 0) return;
            // Avoid duplicate ghosts
            for (var i = 0; i < _ghostSpawnQueue.Count; i++)
                if (_ghostSpawnQueue[i].InstanceId == instanceId)
                    return;
            _ghostSpawnQueue.Add(e);
        }

        public static bool TryDequeueGhost(out Event e)
        {
            if (_ghostSpawnQueue.Count == 0)
            {
                e = default;
                return false;
            }

            e = _ghostSpawnQueue[0];
            _ghostSpawnQueue.RemoveAt(0);
            return true;
        }

        /// <summary>Drop tracking after fade completes (or a lingering card has settled on its zone).</summary>
        public static void Complete(int instanceId) => _byInstance.Remove(instanceId);
    }

    /// <summary>
    /// Presentation-only "result gate" for one Spell/Trap activation (Battle City order: the card springs
    /// up and its art is readable, its name is called out, THEN its effect happens, THEN it lingers or
    /// goes to the Graveyard).
    ///
    /// The engine never waits: <see cref="DuelEngine"/> resolves synchronously and the board state is
    /// already final. This class only tells presentation WHEN each piece may be shown:
    /// <list type="bullet">
    /// <item>result visuals (monster/S-T shatter or removal, LP counters, drawn cards, flips, revived
    /// monsters, GY piles, 2D field) wait for <see cref="ResultAt"/>;</item>
    /// <item>the activating card fades at <see cref="FadeAt"/> (arena and disk toaster alike);</item>
    /// <item>the pacer (AI) and the player's phase / turn advance wait for <see cref="EndAt"/>.</item>
    /// </list>
    /// Removals queued while the engine resolves an activation (<see cref="SetSource"/> /
    /// <see cref="BeginResolve"/> in the same frame) are tagged to that beat, so chain links release their
    /// results one after another in resolution order. Every beat is hard-capped at
    /// <see cref="SpellActivationPresentation.BeatHardCapSeconds"/>.
    /// </summary>
    public static class ActivationBeat
    {
        public sealed class Beat
        {
            public int InstanceId;
            public int CardId;
            public string CardName;
            public bool PlayerSide;
            public bool IsTrap;
            public float RegisteredAt;
            /// <summary>Arena visual began the reveal (claimed): -1 until then.</summary>
            public float SequenceStartedAt = -1f;
            /// <summary>Arena art actually readable: -1 until the visual reports it.</summary>
            public float ArtRevealedAt = -1f;
            public bool Resolved;
            public float ResolvedAt = -1f;
            /// <summary>Chain: this link's results may not show before the previously resolved link's + gap.</summary>
            public float ChainFloor;
            public int Serial;
            /// <summary>
            /// Opened while this earlier beat was still being revealed (same-frame chain): the arena
            /// reveal starts after its art has been readable for <see cref="SpellActivationPresentation.ChainRevealGapSeconds"/>.
            /// </summary>
            public Beat RevealAfter;
            /// <summary>Engine chain link number (0 = not on a chain / unknown).</summary>
            public int ChainLink;
            /// <summary>Negated at resolution (no results of its own).</summary>
            public bool Negated;
            /// <summary>The link whose resolution negated this one (null if unknown).</summary>
            public Beat NegatedBy;
            /// <summary>Attacker whose declaration this activation answered (Mirror Force staging); 0 = none.</summary>
            public int AttackerInstance;
        }

        public struct SummonTrigger
        {
            public CardInstance Card;
            public int InstanceId;
            public int ZoneIndex;
            public bool PlayerSide;
            public float At;
        }

        static readonly List<Beat> _beats = new();
        static readonly Dictionary<int, int> _resultOwner = new(); // result instance → source beat instance
        static readonly List<SummonTrigger> _summonTriggers = new();
        static int _serial;
        static int _sourceInstance;
        static int _sourceFrame = -1;
        static Beat _lastResolved;
        static int _lastResolvedFrame = -1;
        /// <summary>Attacker of the declared attack that is still in progress (0 once the attack ends).</summary>
        static int _attackerInstance;
        /// <summary>Chain link numbers noted before the beat opened (same frame): instance → (link, frame).</summary>
        static readonly Dictionary<int, (int link, int frame)> _pendingChainLink = new();

        static float Now => Time.unscaledTime;

        public static void Clear()
        {
            _beats.Clear();
            _resultOwner.Clear();
            _summonTriggers.Clear();
            _pendingChainLink.Clear();
            _sourceInstance = 0;
            _sourceFrame = -1;
            _lastResolved = null;
            _lastResolvedFrame = -1;
            _attackerInstance = 0;
        }

        // ── engine-side notes (presentation only; never gate legality) ─────────────────────────

        /// <summary>Called from <see cref="SpellActivationPresentation.RegisterActivation"/>.</summary>
        /// <param name="hasLiveEvent">The activation event existed before this registration. An unresolved
        /// beat without one was cancelled / abandoned and is rebuilt instead of reused.</param>
        public static void Open(int instanceId, int cardId, string cardName, bool playerSide, bool isTrap,
            bool hasLiveEvent = true)
        {
            if (instanceId <= 0) return;
            Prune();
            var b = Find(instanceId);
            // Safety net: a stale unresolved beat (activation undone without Cancel) would hand the new
            // activation its old clock — results before the new art, a cut-short read, an expired hover.
            if (b != null && !b.Resolved && !hasLiveEvent)
            {
                Remove(b);
                b = null;
            }

            // Re-registration at resolution (PlaceFaceUp) or FinishSpellTrap(stays) keeps the live beat.
            if (b != null && (!b.Resolved || Now < EndAt(b)))
            {
                if (cardId != 0) b.CardId = cardId;
                if (!string.IsNullOrEmpty(cardName)) b.CardName = cardName;
                b.PlayerSide = playerSide;
                b.IsTrap |= isTrap;
                ApplyPendingChainLink(b);
                return;
            }

            if (b != null) Remove(b);
            var nb = new Beat
            {
                InstanceId = instanceId,
                CardId = cardId,
                CardName = cardName ?? "",
                PlayerSide = playerSide,
                IsTrap = isTrap,
                RegisteredAt = Now,
                Serial = ++_serial,
                RevealAfter = PreviousStillRevealing(),
                AttackerInstance = _attackerInstance
            };
            ApplyPendingChainLink(nb);
            _beats.Add(nb);
        }

        /// <summary>
        /// Newest live beat whose art has not yet been readable for the chain reveal gap: a beat opened now
        /// (typically in the same engine call — AI auto-response) is revealed after it, in link order.
        /// </summary>
        static Beat PreviousStillRevealing()
        {
            var now = Now;
            Beat prev = null;
            for (var i = 0; i < _beats.Count; i++)
            {
                var o = _beats[i];
                if (now >= ArtReadableAt(o) + SpellActivationPresentation.ChainRevealGapSeconds) continue;
                if (prev == null || o.Serial > prev.Serial) prev = o;
            }

            return prev;
        }

        static void ApplyPendingChainLink(Beat b)
        {
            if (b == null || !_pendingChainLink.TryGetValue(b.InstanceId, out var p)) return;
            _pendingChainLink.Remove(b.InstanceId);
            if (p.frame == Time.frameCount) b.ChainLink = p.link;
        }

        /// <summary>The engine undid this activation: drop its beat and everything tagged to it.</summary>
        public static void Cancel(int instanceId)
        {
            _pendingChainLink.Remove(instanceId);
            var b = Find(instanceId);
            if (b != null) Remove(b);
        }

        /// <summary>
        /// True if this instance has an unresolved beat and no effect is resolving in this frame (so a
        /// bounce of it is an undo / rule move, not another card's result).
        /// </summary>
        public static bool IsUnresolvedAndIdle(int instanceId)
        {
            var b = Find(instanceId);
            return b != null && !b.Resolved && CurrentSource() == null;
        }

        static void Remove(Beat b)
        {
            if (b == null) return;
            _beats.Remove(b);
            if (_resultOwner.Count > 0)
            {
                _ownerScratch.Clear();
                foreach (var kv in _resultOwner)
                    if (kv.Value == b.InstanceId)
                        _ownerScratch.Add(kv.Key);
                for (var i = 0; i < _ownerScratch.Count; i++)
                    _resultOwner.Remove(_ownerScratch[i]);
            }

            if (_lastResolved == b) _lastResolved = null;
            if (_sourceInstance == b.InstanceId) _sourceFrame = -1;
            for (var i = 0; i < _beats.Count; i++)
            {
                if (_beats[i].RevealAfter == b) _beats[i].RevealAfter = null;
                if (_beats[i].NegatedBy == b) _beats[i].NegatedBy = null;
            }
        }

        static readonly List<int> _ownerScratch = new();

        /// <summary>Engine chain link number for this activation (ChainStack.AddLink).</summary>
        public static void NoteChainLink(int instanceId, int linkNumber)
        {
            if (instanceId <= 0 || linkNumber <= 0) return;
            var b = Find(instanceId);
            if (b != null && !b.Resolved)
            {
                b.ChainLink = linkNumber;
                return;
            }

            _pendingChainLink[instanceId] = (linkNumber, Time.frameCount);
        }

        /// <summary>This chain link was negated at resolution (by the link resolving in this frame).</summary>
        public static void NoteNegated(int instanceId)
        {
            var b = Find(instanceId);
            if (b == null) return;
            b.Negated = true;
            var src = CurrentSource();
            if (src != null && src != b) b.NegatedBy = src;
        }

        /// <summary>
        /// The engine is applying this card's effect right now (same frame): removals queued until the
        /// frame ends are this beat's results.
        /// </summary>
        public static void SetSource(int instanceId)
        {
            if (instanceId <= 0) return;
            _sourceInstance = instanceId;
            _sourceFrame = Time.frameCount;
        }

        /// <summary>Chain link popped for resolution: source + resolved + chain floor.</summary>
        public static void BeginResolve(int instanceId)
        {
            SetSource(instanceId);
            MarkResolved(instanceId);
        }

        /// <summary>The engine resolved this activation (results, if any, now exist).</summary>
        public static void MarkResolved(int instanceId)
        {
            var b = Find(instanceId);
            if (b == null || b.Resolved) return;
            b.Resolved = true;
            b.ResolvedAt = Now;
            var frame = Time.frameCount;
            if (_lastResolved != null && _lastResolved != b && _lastResolvedFrame == frame &&
                _beats.Contains(_lastResolved))
                b.ChainFloor = Mathf.Max(b.ChainFloor,
                    ResultAt(_lastResolved) + SpellActivationPresentation.ChainLinkResultGapSeconds);
            _lastResolved = b;
            _lastResolvedFrame = frame;
        }

        /// <summary>A card was queued to leave the field (shatter). Tag it to the resolving beat, if any.</summary>
        public static void NoteResult(int resultInstanceId)
        {
            if (resultInstanceId <= 0) return;
            var src = CurrentSource();
            if (src == null || src.InstanceId == resultInstanceId) return;
            _resultOwner[resultInstanceId] = src.InstanceId;
        }

        /// <summary>
        /// The summoned monster that opened a response window. If a trap (Trap Hole) removes it in the
        /// same call, the arena never saw it — this lets the arena stage it before the result.
        /// </summary>
        public static void NoteSummonTrigger(CardInstance summoned, int zoneIndex, bool playerSide)
        {
            if (summoned == null || summoned.InstanceId <= 0) return;
            _summonTriggers.RemoveAll(t => t.InstanceId == summoned.InstanceId || Now - t.At > 1f);
            _summonTriggers.Add(new SummonTrigger
            {
                Card = summoned,
                InstanceId = summoned.InstanceId,
                ZoneIndex = zoneIndex,
                PlayerSide = playerSide,
                At = Now
            });
        }

        /// <summary>The attacker whose declaration opened a response window (Mirror Force staging).</summary>
        public static void NoteAttackTrigger(CardInstance attacker)
        {
            if (attacker == null) return;
            _attackerInstance = attacker.InstanceId;
        }

        /// <summary>
        /// The declared attack is over (resolved, negated, fizzled). Activations opened after this are
        /// not answering it; beats already stamped keep their attacker until their result is shown.
        /// </summary>
        public static void NoteAttackEnded() => _attackerInstance = 0;

        // ── presentation-side reports ───────────────────────────────────────────────────────────

        /// <summary>An arena visual started the reveal for this instance (prevents duplicate ghosts).</summary>
        /// <summary>
        /// The activation is only now being shown: the opponent's hand revealed the card after the engine had
        /// already activated it (the AI commits first). Its beat — art readable, results, fade — counts from now,
        /// unless the arena has already started presenting it.
        /// </summary>
        public static void RestartClock(int instanceId)
        {
            var b = Find(instanceId);
            if (b == null || b.SequenceStartedAt >= 0f) return;
            b.RegisteredAt = Mathf.Max(b.RegisteredAt, Now);
        }

        public static void NoteSequenceStarted(int instanceId)
        {
            var b = Find(instanceId);
            if (b != null && b.SequenceStartedAt < 0f)
                b.SequenceStartedAt = Now;
        }

        /// <summary>The arena face art is now readable. Results are gated from this moment.</summary>
        public static void NoteArtRevealed(int instanceId)
        {
            var b = Find(instanceId);
            if (b != null && b.ArtRevealedAt < 0f)
                b.ArtRevealedAt = Now;
        }

        public static bool IsClaimed(int instanceId)
        {
            var b = Find(instanceId);
            return b != null && b.SequenceStartedAt >= 0f;
        }

        public static bool TryTakeSummonTrigger(out SummonTrigger trigger)
        {
            for (var i = _summonTriggers.Count - 1; i >= 0; i--)
            {
                if (Now - _summonTriggers[i].At > 1f)
                {
                    _summonTriggers.RemoveAt(i);
                    continue;
                }

                trigger = _summonTriggers[i];
                _summonTriggers.RemoveAt(i);
                return true;
            }

            trigger = default;
            return false;
        }

        /// <summary>
        /// Attacker to keep charged while its fate (Mirror Force / Magic Cylinder) is still hidden.
        /// Only beats opened while that attack was still declared carry it (was: any result gate within
        /// 20 s of any attack re-charged the last attacker).
        /// </summary>
        public static int StagedAttackerInstance
        {
            get
            {
                var now = Now;
                for (var i = 0; i < _beats.Count; i++)
                {
                    var b = _beats[i];
                    if (b.AttackerInstance != 0 && b.Resolved && now < ResultAt(b))
                        return b.AttackerInstance;
                }

                return 0;
            }
        }

        // ── timeline queries ───────────────────────────────────────────────────────────────────

        public static float ArtReadableAt(Beat b) => ArtReadableAt(b, 0);

        static float ArtReadableAt(Beat b, int depth)
        {
            if (b.ArtRevealedAt >= 0f) return b.ArtRevealedAt;
            var start = b.SequenceStartedAt >= 0f ? b.SequenceStartedAt : b.RegisteredAt;
            var t = start + SpellActivationPresentation.ArtReadableDelaySeconds;
            // Staggered chain link: the arena rise starts at the reveal floor, art ~0.49 s into the rise.
            var floor = RevealFloor(b, depth);
            if (floor > 0f)
                t = Mathf.Max(t, floor + SpellActivationPresentation.RiseSeconds *
                    SpellActivationPresentation.RiseArtRevealFraction);
            return t;
        }

        static float RevealFloor(Beat b, int depth)
        {
            if (b.RevealAfter == null || depth > 16) return 0f;
            return ArtReadableAt(b.RevealAfter, depth + 1) + SpellActivationPresentation.ChainRevealGapSeconds;
        }

        /// <summary>
        /// Earliest time the arena may start this activation's rise (0 = no floor). Links opened in the
        /// same engine call rise one after another instead of together.
        /// </summary>
        public static float RevealStartFloor(int instanceId)
        {
            var b = Find(instanceId);
            return b != null ? RevealFloor(b, 0) : 0f;
        }

        /// <summary>When this beat's reveal (and its name call-out) starts.</summary>
        static float RevealStartAt(Beat b) => Mathf.Max(b.RegisteredAt, RevealFloor(b, 0));

        /// <summary>
        /// May this activating card's own shatter play now? A resolved, non-negated link destroyed by a
        /// later link (MST on the Trap it answers) first shows its OWN effect with the card on screen,
        /// then shatters <see cref="SpellActivationPresentation.OwnResultBeforeShatterSeconds"/> later.
        /// Also requires the destroying card's result beat (<see cref="IsRemovalReleased"/>).
        /// </summary>
        public static bool IsShatterReleased(int instanceId)
        {
            if (!IsRemovalReleased(instanceId)) return false;
            var b = Find(instanceId);
            if (b == null || !b.Resolved || b.Negated) return true;
            return Now >= ResultAt(b) + SpellActivationPresentation.OwnResultBeforeShatterSeconds;
        }

        static float Cap(Beat b) => b.RegisteredAt + SpellActivationPresentation.BeatHardCapSeconds;

        /// <summary>When this beat's results may be presented.</summary>
        public static float ResultAt(Beat b)
        {
            var before = b.PlayerSide
                ? SpellActivationPresentation.ReadBeforeResultSeconds
                : SpellActivationPresentation.OpponentReadBeforeResultSeconds;
            var t = Mathf.Max(ArtReadableAt(b) + before, b.ChainFloor);
            return Mathf.Min(t, Cap(b) - SpellActivationPresentation.FadeToGySeconds);
        }

        /// <summary>When a one-shot starts fading (or a lingering card starts settling).</summary>
        public static float FadeAt(Beat b)
        {
            var after = b.PlayerSide
                ? SpellActivationPresentation.ReadAfterResultSeconds
                : SpellActivationPresentation.OpponentReadAfterResultSeconds;
            var minRead = b.PlayerSide
                ? SpellActivationPresentation.MinHoverSeconds
                : SpellActivationPresentation.OpponentReadHoverSeconds;
            var resolvedAt = b.Resolved ? b.ResolvedAt : b.RegisteredAt;
            var t = Mathf.Max(Mathf.Max(ResultAt(b), resolvedAt) + after, ArtReadableAt(b) + minRead);
            return Mathf.Min(t, Cap(b) - SpellActivationPresentation.FadeToGySeconds);
        }

        /// <summary>Whole beat over (card gone to GY / settled).</summary>
        public static float EndAt(Beat b) =>
            Mathf.Min(FadeAt(b) + SpellActivationPresentation.FadeToGySeconds, Cap(b));

        public static Beat Find(int instanceId)
        {
            if (instanceId <= 0) return null;
            for (var i = _beats.Count - 1; i >= 0; i--)
                if (_beats[i].InstanceId == instanceId)
                    return _beats[i];
            return null;
        }

        static Beat CurrentSource() =>
            _sourceFrame == Time.frameCount ? Find(_sourceInstance) : null;

        /// <summary>
        /// Fade time for the arena visual. False = no beat (legacy fixed hover).
        /// <paramref name="waitingOnField"/>: the card still sits in its zone — an unresolved beat keeps
        /// it presented (chain window / target pick) until it resolves or the unresolved cap passes.
        /// </summary>
        public static bool TryGetFadeAt(int instanceId, bool waitingOnField, out float fadeAt)
        {
            fadeAt = 0f;
            var b = Find(instanceId);
            if (b == null) return false;
            if (waitingOnField && !b.Resolved &&
                Now < b.RegisteredAt + SpellActivationPresentation.UnresolvedHoverCapSeconds)
            {
                fadeAt = float.PositiveInfinity;
                return true;
            }

            fadeAt = FadeAt(b);
            return true;
        }

        /// <summary>
        /// True while any resolved beat has results that are not presented yet. Untagged result
        /// visuals (LP, draws, flips, revived monsters, 2D field, GY piles) wait on this.
        /// </summary>
        public static bool AnyResultPending
        {
            get
            {
                var now = Now;
                for (var i = 0; i < _beats.Count; i++)
                {
                    var b = _beats[i];
                    if (b.Resolved && now < ResultAt(b)) return true;
                }

                // Mid-resolution in this very frame (MarkResolved comes after ApplyClause).
                var src = CurrentSource();
                return src != null && now < ResultAt(src);
            }
        }

        /// <summary>Latest time an untagged pending result is released (now if none).</summary>
        public static float PendingResultGate
        {
            get
            {
                var now = Now;
                var gate = now;
                var src = CurrentSource();
                if (src != null) gate = Mathf.Max(gate, ResultAt(src));
                for (var i = 0; i < _beats.Count; i++)
                {
                    var b = _beats[i];
                    if (b.Resolved && now < ResultAt(b))
                        gate = Mathf.Max(gate, ResultAt(b));
                }

                return gate;
            }
        }

        /// <summary>May the visual for this removed card (shatter / bounce / destroy) play now?</summary>
        public static bool IsRemovalReleased(int instanceId)
        {
            if (instanceId > 0 && _resultOwner.TryGetValue(instanceId, out var owner))
            {
                var b = Find(owner);
                if (b != null)
                    return Now >= ResultAt(b);
                _resultOwner.Remove(instanceId);
            }

            return !AnyResultPending;
        }

        /// <summary>Pacer: latest end over all live beats (0 if none). Never shortens an in-flight hold.</summary>
        public static float HoldUntilUnscaled
        {
            get
            {
                var until = 0f;
                for (var i = 0; i < _beats.Count; i++)
                    until = Mathf.Max(until, EndAt(_beats[i]));
                return until;
            }
        }

        /// <summary>Player phase / turn advance (and new attack declarations) wait for the whole beat.</summary>
        public static bool BlocksAdvance(out string cardName, out float secondsLeft)
        {
            cardName = null;
            secondsLeft = 0f;
            var now = Now;
            Beat latest = null;
            for (var i = 0; i < _beats.Count; i++)
            {
                var b = _beats[i];
                var end = EndAt(b);
                if (now >= end) continue;
                if (end - now > secondsLeft)
                {
                    secondsLeft = end - now;
                    latest = b;
                }
            }

            if (latest == null) return false;
            cardName = latest.CardName;
            return true;
        }

        /// <summary>Hint line while the beat owns the pacer.</summary>
        public static string HoldReason()
        {
            var b = NewestLive();
            if (b == null) return "";
            var name = string.IsNullOrEmpty(b.CardName) ? (b.IsTrap ? "a Trap" : "a Spell") : b.CardName;
            return b.PlayerSide
                ? $"{name} — watch it resolve…"
                : $"Opponent activates {name} — watch the field…";
        }

        static Beat NewestLive()
        {
            var now = Now;
            Beat best = null;
            for (var i = 0; i < _beats.Count; i++)
            {
                var b = _beats[i];
                if (now >= EndAt(b)) continue;
                if (best == null || b.Serial > best.Serial) best = b;
            }

            return best;
        }

        /// <summary>
        /// Name banner, chosen by phase (anime order: links revealed in order, resolved in reverse):
        /// <list type="number">
        /// <item>a resolved link whose result is about to land / just landed (the link resolving now —
        /// earliest such result);</item>
        /// <item>else the newest activation whose reveal has started and whose result is still ahead
        /// (being revealed / waiting on a chain window or target pick);</item>
        /// <item>else the activation whose result was shown most recently, until it fades.</item>
        /// </list>
        /// <paramref name="chainDepth"/> is the engine's chain link number when a multi-link chain is
        /// live (0 otherwise), not a count of overlapping beats.
        /// </summary>
        public static bool TryGetBanner(out string cardName, out bool isTrap, out bool playerSide,
            out int chainDepth, out int serial)
        {
            cardName = null;
            isTrap = false;
            playerSide = false;
            chainDepth = 0;
            serial = 0;
            var now = Now;
            var lead = SpellActivationPresentation.BannerResultLeadSeconds;
            var tail = SpellActivationPresentation.BannerResultTailSeconds;
            Beat resolving = null, revealing = null, recent = null;
            float resolvingAt = float.MaxValue, recentAt = float.MinValue;
            var maxLink = 0;
            for (var i = 0; i < _beats.Count; i++)
            {
                var b = _beats[i];
                // Unresolved (chain window / target pick): up as long as its hologram presents.
                var bannerEnd = b.Resolved
                    ? FadeAt(b)
                    : Mathf.Max(FadeAt(b), b.RegisteredAt + SpellActivationPresentation.UnresolvedHoverCapSeconds);
                if (now >= bannerEnd) continue;
                if (b.ChainLink > maxLink) maxLink = b.ChainLink;
                if (now < RevealStartAt(b)) continue;
                var r = ResultAt(b);
                if (b.Resolved && !b.Negated && now >= r - lead && now < r + tail && r < resolvingAt)
                {
                    resolving = b;
                    resolvingAt = r;
                }

                // A negated link's own window ends when its negation is shown.
                var end = b.Negated && b.NegatedBy != null ? ResultAt(b.NegatedBy) : r;
                if ((!b.Resolved || now < end) && (revealing == null || b.Serial > revealing.Serial))
                    revealing = b;
                if (b.Resolved && !b.Negated && now >= r && r > recentAt)
                {
                    recent = b;
                    recentAt = r;
                }
            }

            var best = resolving ?? revealing ?? recent;
            if (best == null) return false;
            cardName = best.CardName;
            isTrap = best.IsTrap;
            playerSide = best.PlayerSide;
            serial = best.Serial;
            chainDepth = maxLink >= 2 ? best.ChainLink : 0;
            return true;
        }

        /// <summary>Snapshot of live beats (arena generic-ghost pass).</summary>
        public static void CollectLive(List<Beat> dst)
        {
            if (dst == null) return;
            dst.Clear();
            Prune();
            dst.AddRange(_beats);
        }

        static void Prune()
        {
            var now = Now;
            for (var i = _beats.Count - 1; i >= 0; i--)
            {
                var b = _beats[i];
                // Unresolved (target pick / chain window) beats live until the hard cap so their
                // eventual resolution is still gated and faded on the beat clock.
                var until = b.Resolved ? EndAt(b) : Cap(b);
                if (now < until + 1f) continue;
                Remove(b);
            }

            if (_resultOwner.Count > 0 && _beats.Count == 0)
                _resultOwner.Clear();
        }
    }
}
