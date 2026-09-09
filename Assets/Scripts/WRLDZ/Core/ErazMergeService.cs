using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>
    /// ERAZ badges ship as 5 shards. Spend Set Energy of an already-owned era
    /// to fuse them into the full badge, which unlocks that era's tablets.
    /// Fortune-teller tutorial grants Original whole — never as shards.
    /// </summary>
    public static class ErazMergeService
    {
        public const int PiecesRequired = 5;
        public const int SetEnergyCost = 2500;

        public static int PieceCount(PlayerInventory inv, string eraId) =>
            ArtifactService.Qty(inv, ArtifactService.ErazPieceId(eraId));

        public static bool TryMerge(PlayerProgress p, PlayerInventory inv, string eraId, out string error)
        {
            error = null;
            if (p == null || inv == null)
            {
                error = "No inventory.";
                return false;
            }

            eraId = (eraId ?? "").Trim();
            if (string.IsNullOrEmpty(eraId))
            {
                error = "No era.";
                return false;
            }

            if (ErazProgress.HasBadge(p, eraId))
            {
                error = "Badge already whole.";
                return false;
            }

            if (string.Equals(eraId, ErazFormat.Original, System.StringComparison.OrdinalIgnoreCase))
            {
                error = "Original is granted by the fortune teller.";
                return false;
            }

            var pieceId = ArtifactService.ErazPieceId(eraId);
            if (ArtifactService.Qty(inv, pieceId) < PiecesRequired)
            {
                error = $"Need {PiecesRequired} shards (have {ArtifactService.Qty(inv, pieceId)}).";
                return false;
            }

            if (!TrySpendAnyOwnedSe(p, inv, SetEnergyCost, out error))
                return false;

            if (!ArtifactService.TrySpend(p, inv, pieceId, PiecesRequired, out error))
            {
                RefundSe(p, inv, SetEnergyCost);
                return false;
            }

            ErazProgress.GrantBadge(p, eraId);
            ArtifactService.Grant(p, inv, ArtifactService.ErazId(eraId), 1);
            return true;
        }

        static bool TrySpendAnyOwnedSe(PlayerProgress p, PlayerInventory inv, int cost, out string error)
        {
            error = null;
            if (ArtifactService.SetEnergyTotal(inv) < cost)
            {
                error = $"Need {cost} Set Energy of an unlocked era.";
                return false;
            }

            var left = cost;
            var instances = inv.artifactDeckBox?.instances;
            if (instances != null)
            {
                foreach (var inst in instances)
                {
                    if (left <= 0) break;
                    if (inst == null || string.IsNullOrEmpty(inst.defId)) continue;
                    if (!inst.defId.StartsWith("se.", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var set = inst.defId.Substring(3);
                    if (!ArtifactService.CanEarnSetEnergy(p, set)) continue;
                    var take = Mathf.Min(left, inst.qty);
                    if (take <= 0) continue;
                    if (!ArtifactService.TrySpend(p, inv, inst.defId, take, out error))
                        return false;
                    left -= take;
                }
            }

            if (left > 0)
            {
                error = $"Need {cost} Set Energy of an unlocked era.";
                return false;
            }

            return true;
        }

        static void RefundSe(PlayerProgress p, PlayerInventory inv, int amount) =>
            ArtifactService.GrantUntaggedSetEnergy(p, inv, amount);
    }
}
