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
        /// <summary>Air gap between card bodies, as a fraction of scaled width.</summary>
        public const float BodyGapFrac = 0.08f;

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
            SeparateOverlappingCards();
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
            ComputeFanSlot(index, total, CardScale, out var local, out var rot, out var scale);
            fc.SetHandRest(local, rot, scale);
        }

        /// <summary>
        /// Rest pose for one card in a physical fan. Centers are far enough apart that
        /// the PhysicalCard boxes do not occupy the same volume (yaw included).
        /// </summary>
        public static void ComputeFanSlot(int index, int total, float cardScale,
            out Vector3 localPos, out Quaternion localRot, out Vector3 localScale)
        {
            total = Mathf.Max(1, total);
            index = Mathf.Clamp(index, 0, total - 1);
            var scale = cardScale * Mathf.Lerp(1f, 0.86f, Mathf.InverseLerp(5f, 10f, total));
            var yawSpan = Mathf.Lerp(8f, 16f, Mathf.InverseLerp(1f, 7f, total));
            var t = total == 1 ? 0.5f : index / (float)(total - 1);
            var yaw = Mathf.Lerp(yawSpan, -yawSpan, t);
            var spacing = RestCenterSpacing(total, scale, yawSpan);
            var x = (index - (total - 1) * 0.5f) * spacing;
            var z = (index - (total - 1) * 0.5f) * RestDepthSpacing(scale, yawSpan);
            localPos = new Vector3(x, 0f, z);
            // Art normal is local +Z. Volume faces torso.forward; flip 180° so art faces the camera.
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
        public static bool RestBodiesOverlap(int i, int j, int total, float cardScale)
        {
            if (i == j) return false;
            ComputeFanSlot(i, total, cardScale, out var pa, out var ra, out var sa);
            ComputeFanSlot(j, total, cardScale, out var pb, out var rb, out var sb);
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
