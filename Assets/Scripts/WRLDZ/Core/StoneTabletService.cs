using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>
    /// 1,000 Set Energy of set X → 10 random cards from that set.
    /// Only sets in an owned ERAZ band can be offered.
    /// </summary>
    public static class StoneTabletService
    {
        public const int PackCost = 1000;
        public const int PackSize = 10;

        public static bool TryOpen(
            PlayerProgress p,
            PlayerInventory inv,
            string setCode,
            out int[] cardIds,
            out string error)
        {
            cardIds = System.Array.Empty<int>();
            error = null;
            if (p == null || inv == null)
            {
                error = "No inventory.";
                return false;
            }

            setCode = (setCode ?? "").Trim().ToUpperInvariant();
            if (setCode.Length == 0)
            {
                error = "No set.";
                return false;
            }

            if (!ArtifactService.CanEarnSetEnergy(p, setCode))
            {
                error = "Need the ERAZ badge for that era.";
                return false;
            }

            var seId = ArtifactService.SetEnergyId(setCode);
            if (!ArtifactService.TrySpend(p, inv, seId, PackCost, out error))
                return false;

            var pool = PoolForSet(setCode);
            if (pool.Count == 0)
            {
                ArtifactService.Grant(p, inv, seId, PackCost);
                error = "No cards in that set yet.";
                return false;
            }

            var rng = new System.Random();
            var ids = new int[PackSize];
            var now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            for (var i = 0; i < PackSize; i++)
            {
                ids[i] = pool[rng.Next(pool.Count)];
                SoulCardService.TryReceiveTcgCard(p, inv, ids[i], 1, atHome: true, nowUnix: now,
                    out _, out _);
            }

            cardIds = ids;
            return true;
        }

        public static List<int> PoolForSet(string setCode)
        {
            var list = new List<int>();
            var file = CardEraCurriculum.Load();
            if (file?.sets == null) return list;
            for (var i = 0; i < file.sets.Length; i++)
            {
                var s = file.sets[i];
                if (s == null || !string.Equals(s.code, setCode, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var ids = s.priorityPasscodes;
                if (ids == null) break;
                var db = CardDatabase.Instance ?? CardDatabase.Load();
                for (var j = 0; j < ids.Length; j++)
                {
                    if (ids[j] <= 0) continue;
                    if (db != null && db.Get(ids[j]) == null) continue;
                    list.Add(ids[j]);
                }

                break;
            }

            return list;
        }
    }
}
