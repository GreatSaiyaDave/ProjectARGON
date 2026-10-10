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
            var have = ArtifactService.Qty(inv, seId);
            if (have < PackCost)
            {
                error = $"Need {PackCost} {setCode} Set Energy (have {have}).";
                return false;
            }

            var pool = PoolForSet(setCode);
            if (pool.Count == 0)
            {
                error = "No cards in that set yet.";
                return false;
            }

            if (inv.pendingReceiveQty > 0)
            {
                error = "Make room for the card still waiting first.";
                return false;
            }

            var rng = new System.Random();
            var ids = new int[PackSize];
            for (var i = 0; i < PackSize; i++)
                ids[i] = pool[rng.Next(pool.Count)];

            // Every card must land (home boxes, then soul tiles) BEFORE any Set Energy is spent. Rehearse the
            // exact placements on a copy of the inventory; a full collection fails here with nothing lost
            // (QC F11 — it used to spend 1000 SE and silently keep only the last overflow card).
            var now = ClockGuard.NowUnix(p);
            if (!AllFit(inv, ids, now))
            {
                error = $"Make room for {PackSize} cards first (home boxes or backpack).";
                return false;
            }

            if (!ArtifactService.TrySpend(p, inv, seId, PackCost, out error))
                return false;

            for (var i = 0; i < PackSize; i++)
            {
                if (SoulCardService.TryReceiveTcgCard(p, inv, ids[i], 1, atHome: true, nowUnix: now,
                        out var left, out var why) && left <= 0)
                    continue;
                // Unreachable after the rehearsal; kept loud so a placement-rule change cannot lose cards quietly.
                Debug.LogError($"[WRLDZ] Tablet pack card {ids[i]} did not fit after rehearsal: {why}");
            }

            cardIds = ids;
            return true;
        }

        static bool AllFit(PlayerInventory inv, int[] ids, long nowUnix)
        {
            var copy = JsonUtility.FromJson<PlayerInventory>(JsonUtility.ToJson(inv));
            if (copy == null) return false;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!SoulCardService.TryReceiveTcgCard(null, copy, ids[i], 1, atHome: true, nowUnix: nowUnix,
                        out var left, out _) || left > 0 || copy.pendingReceiveQty > 0)
                    return false;
            }

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
