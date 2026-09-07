using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Presentation bus for Spell/Trap field holograms.
    ///
    /// Yugipedia <c>Activate</c>: a Spell/Trap is played face-up from hand
    /// or <b>flipped from face-down on the field</b>.
    /// Yugipedia <c>Duel Disk</c>: Set S/T cards eject from the slot to stand
    /// face-up. Arena Solid Vision is that same beat on the street hologram:
    /// the Set card rotates from the pad into an upright hover, card back to
    /// the controller, art toward the opponent.
    ///
    /// One-shots fade to GY after the hover. Continuous / Field / Equip /
    /// lingering effects (e.g. Swords) stay in that upright pose.
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

        /// <summary>How long the full activate rise+reveal should take (presentation).</summary>
        public const float ActivateSequenceSeconds = 1.65f;

        /// <summary>
        /// Hover hold after a one-shot resolves, before its hologram leaves to GY
        /// (fade / shatter). The rise already reveals the art ~0.9s in, so this only
        /// needs to be a short read beat — a longer hold reads as the hologram
        /// lingering "a second too long" after the card has already resolved.
        /// </summary>
        public const float MinHoverSeconds = 0.2f;

        /// <summary>Fade duration when leaving to GY after activation.</summary>
        public const float FadeToGySeconds = 0.75f;

        static readonly Dictionary<int, Event> _byInstance = new();
        static readonly List<Event> _ghostSpawnQueue = new();

        public static void Clear()
        {
            _byInstance.Clear();
            _ghostSpawnQueue.Clear();
        }

        /// <summary>Called when a Spell/Trap is placed face-up for activation.</summary>
        public static void RegisterActivation(
            int instanceId,
            int cardId,
            string cardName,
            bool playerSide,
            int zoneIndex,
            bool staysOnField,
            bool flatOnBoard = false)
        {
            if (instanceId <= 0) return;
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
        }

        /// <summary>Destroyed by effect (MST etc.) — prefer shatter over activation fade.</summary>
        public static void CancelActivationPresentation(int instanceId)
        {
            if (instanceId <= 0) return;
            _byInstance.Remove(instanceId);
        }

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

        /// <summary>Drop tracking after fade completes.</summary>
        public static void Complete(int instanceId) => _byInstance.Remove(instanceId);
    }
}
