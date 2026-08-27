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
    /// Spawns hand cards as floating holograms with fan layout.
    /// </summary>
    public class ArHandVolume : MonoBehaviour
    {
        public IArTrackingSource Tracker;
        /// <summary>Player disk — draws originate from this deck well.</summary>
        public ArDuelDiskRig SourceDeck;
        /// <summary>Far-side AI / P2 hand — backs toward the local camera, not tappable.</summary>
        public bool IsOpponent;
        /// <summary>Local player sees faces; the opposite hand is public backs only.</summary>
        public bool RevealFaces = true;
        public int Layer = 28;
        public Color Tint = new(0.55f, 0.95f, 1f);
        public float FanWidth = 0.72f;
        public float CardDepth = 0.08f;
        /// <summary>Holo size in stage units — large enough for EditorSim / phone RT picks.</summary>
        public float CardScale = 0.22f;

        public Transform VolumeRoot { get; private set; }
        readonly List<ArFloatingCard> _cards = new();
        readonly Dictionary<int, ArFloatingCard> _byInstance = new();

        public IReadOnlyList<ArFloatingCard> Cards => _cards;

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
            hv.RevealFaces = !opponent;
            hv.VolumeRoot = new GameObject("Cards").transform;
            hv.VolumeRoot.SetParent(go.transform, false);
            // No volume guide cube — it read as a messy cyan box in the AR stage
            return hv;
        }

        void LateUpdate()
        {
            if (Tracker == null) return;
            Pose torso;
            var ok = IsOpponent
                ? Tracker.TryGetOpponentTorso(out torso)
                : Tracker.TryGetTorso(out torso);
            if (!ok) return;
            // Volume in front of chest, slightly down toward the worn disk
            // (human ready stance: hand holos between eyes and left-arm plate)
            var pos = torso.position + torso.forward * 0.28f + torso.up * -0.12f;
            transform.SetPositionAndRotation(pos, torso.rotation);
        }

        public void SyncHand(DuelistState who, CardDatabase db)
        {
            if (who == null || db == null) return;
            var live = new HashSet<int>();
            var arriveSlot = 0;
            var arrivedThisSync = 0;
            for (var i = 0; i < who.Hand.Count; i++)
            {
                var card = who.Hand[i];
                if (card == null) continue;
                live.Add(card.InstanceId);
                if (!_byInstance.TryGetValue(card.InstanceId, out var fc) || fc == null)
                {
                    fc = ArFloatingCard.Spawn(VolumeRoot, card, db, Layer, Tint);
                    fc.KeepFaceHidden = !RevealFaces;
                    _byInstance[card.InstanceId] = fc;
                    _cards.Add(fc);
                    LayoutCard(fc, i, who.Hand.Count);
                    fc.RefreshArtFace(showFace: RevealFaces);
                    if (Application.isPlaying && gameObject.activeInHierarchy)
                    {
                        BeginArriveFromDeck(fc, arriveSlot++);
                        arrivedThisSync++;
                    }
                    continue;
                }

                fc.Card = card;

                // Don't fight free-roam drag; layout only updates rest pose (soft spring)
                if (fc.IsLockedToZone || fc.IsDragging || fc.IsDrawArriving) continue;
                LayoutCard(fc, i, who.Hand.Count);
            }

            // Remove hand tracking for cards that left the hand.
            // Zone-locked cards stay in the scene (parented to disk) — do not Destroy.
            var dead = new List<int>();
            foreach (var kv in _byInstance)
            {
                if (live.Contains(kv.Key)) continue;
                if (kv.Value != null && !kv.Value.IsLockedToZone)
                    ArObjectUtil.Destroy(kv.Value.gameObject);
                dead.Add(kv.Key);
            }

            foreach (var id in dead)
            {
                _byInstance.Remove(id);
                _cards.RemoveAll(c => c == null || c.Card == null || c.Card.InstanceId == id);
            }

            if (arrivedThisSync > 0 && !IsOpponent)
                DuelPresentationPacer.HoldDraw(arrivedThisSync);
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
            total = Mathf.Max(1, total);
            var t = total == 1 ? 0.5f : index / (float)(total - 1);
            var x = Mathf.Lerp(-FanWidth * 0.5f, FanWidth * 0.5f, t);
            var yaw = Mathf.Lerp(18f, -18f, t);
            var local = new Vector3(x, 0f, -index * 0.012f);
            // Art normal is local +Z (ArPhysicalCardBuilder). Volume faces torso.forward (+Z)
            // toward midfield; camera looks the same way — so flip 180° yaw so art faces the camera.
            var rot = Quaternion.Euler(-8f, yaw + 180f, 0f);
            var scale = Vector3.one * CardScale;
            fc.SetHandRest(local, rot, scale);
        }

        public ArFloatingCard PickByRay(Ray ray, float maxDist = 3f)
        {
            if (IsOpponent) return null;
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
            if (IsOpponent) return null;
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
            if (IsOpponent) return null;
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

        public void RemoveCard(ArFloatingCard fc)
        {
            if (fc?.Card == null) return;
            _byInstance.Remove(fc.Card.InstanceId);
            _cards.Remove(fc);
        }
    }
}
