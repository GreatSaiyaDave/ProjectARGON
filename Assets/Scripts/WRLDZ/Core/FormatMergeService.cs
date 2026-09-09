using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>Same fuse as ERAZ: 5 shards + 2,500 SE of an unlocked era.</summary>
    public static class FormatMergeService
    {
        public const int PiecesRequired = ErazMergeService.PiecesRequired;
        public const int SetEnergyCost = ErazMergeService.SetEnergyCost;

        public static int PieceCount(PlayerInventory inv, string formatId) =>
            ArtifactService.Qty(inv, ArtifactService.FormatPieceId(formatId));

        public static bool TryMerge(PlayerProgress p, PlayerInventory inv, string formatId, out string error)
        {
            error = null;
            if (p == null || inv == null)
            {
                error = "No inventory.";
                return false;
            }

            formatId = (formatId ?? "").Trim();
            if (string.IsNullOrEmpty(formatId))
            {
                error = "No format.";
                return false;
            }

            if (FormatProgress.HasBadge(p, formatId))
            {
                error = "Badge already whole.";
                return false;
            }

            var pieceId = ArtifactService.FormatPieceId(formatId);
            if (ArtifactService.Qty(inv, pieceId) < PiecesRequired)
            {
                error = $"Need {PiecesRequired} shards (have {ArtifactService.Qty(inv, pieceId)}).";
                return false;
            }

            if (ArtifactService.SetEnergyTotal(inv) < SetEnergyCost)
            {
                error = $"Need {SetEnergyCost} Set Energy of an unlocked era.";
                return false;
            }

            if (!ErazMergeServiceSpend(p, inv, out error))
                return false;

            if (!ArtifactService.TrySpend(p, inv, pieceId, PiecesRequired, out error))
            {
                ArtifactService.GrantUntaggedSetEnergy(p, inv, SetEnergyCost);
                return false;
            }

            FormatProgress.GrantBadge(p, formatId);
            ArtifactService.Grant(p, inv, ArtifactService.FormatId(formatId), 1);
            return true;
        }

        static bool ErazMergeServiceSpend(PlayerProgress p, PlayerInventory inv, out string error)
        {
            error = null;
            var left = SetEnergyCost;
            var instances = inv.artifactDeckBox?.instances;
            if (instances == null)
            {
                error = $"Need {SetEnergyCost} Set Energy of an unlocked era.";
                return false;
            }

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

            if (left > 0)
            {
                error = $"Need {SetEnergyCost} Set Energy of an unlocked era.";
                return false;
            }

            return true;
        }
    }
}
