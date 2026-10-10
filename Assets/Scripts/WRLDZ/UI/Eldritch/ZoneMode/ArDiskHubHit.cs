using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Zone Mode tap target on the Spirit Dueler hub (wrist cuff / deck well side of the disk).
    /// Same pattern as <see cref="ArDiskPhaseHit"/>: BoxCollider isTrigger on the disk layer,
    /// found via <c>GetComponentInParent</c> after <c>Physics.RaycastAll</c>.
    /// Parented to the rig root (the tracked left-forearm pose) rather than DiskRoot so the
    /// target stays put while the blade folds and unfolds.
    /// </summary>
    public class ArDiskHubHit : MonoBehaviour
    {
        public const string HitName = "HubHit";

        /// <summary>Rig-local metres (× <see cref="ArPlaymatLayout.DiskMeshVisualScale"/>); generous on purpose — thumbs, not styluses.</summary>
        public static readonly Vector3 DefaultSize = new(0.20f, 0.10f, 0.18f);

        /// <summary>Deck well sits on the wrist hover height (see <see cref="SpiritDuelerSkin.WristHoverY"/>).</summary>
        public static Vector3 DefaultCenter => new(0f, SpiritDuelerSkin.WristHoverY, 0f);

        public ArDuelDiskRig Disk;

        /// <summary>
        /// Build (or reuse) the hub trigger under the rig root. Idempotent.
        /// </summary>
        public static ArDiskHubHit Attach(ArDuelDiskRig disk, Vector3? size = null, Vector3? localCenter = null)
        {
            if (disk == null) return null;
            var parent = disk.transform;

            var existing = parent.Find(HitName);
            if (existing != null)
            {
                var prior = existing.GetComponent<ArDiskHubHit>();
                if (prior == null) prior = existing.gameObject.AddComponent<ArDiskHubHit>();
                prior.Disk = disk;
                existing.gameObject.layer = disk.Layer;
                return prior;
            }

            var go = new GameObject(HitName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter ?? DefaultCenter;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = disk.Layer;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = (size ?? DefaultSize) * ArPlaymatLayout.DiskMeshVisualScale;

            var hit = go.AddComponent<ArDiskHubHit>();
            hit.Disk = disk;
            return hit;
        }
    }
}
