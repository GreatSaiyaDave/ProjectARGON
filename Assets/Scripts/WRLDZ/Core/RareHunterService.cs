using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Rare Hunter defeat stakes:
    /// 1) Random unused inventory card (not in deck boxes, not Tome pages)
    /// 2) Else flat digizeni fee
    /// </summary>
    public static class RareHunterService
    {
        public const int FlatFeeDigizeni = 250;

        public struct LossResult
        {
            public bool tookCard;
            public int cardId;
            public bool tookCurrency;
            public int digizeniTaken;
            public string message;
        }

        public static LossResult ApplyDefeatPenalty(LocalAccountStore.Account acc)
        {
            var result = new LossResult { message = "No penalty applied." };
            if (acc == null) return result;
            acc.EnsureProgress();
            acc.EnsureInventory();

            var unused = acc.inventory.ListUnusedCardIds();
            if (unused.Count > 0)
            {
                var pick = unused[Random.Range(0, unused.Count)];
                if (acc.inventory.TryRemoveCards(pick, 1))
                {
                    result.tookCard = true;
                    result.cardId = pick;
                    result.message = $"Rare Hunter stole card #{pick} from your binder.";
                    ProgressionService.Persist(acc);
                    return result;
                }
            }

            // Fallback: flat currency
            var fee = FlatFeeDigizeni;
            var pay = Mathf.Min(fee, ArtifactService.Qty(acc.inventory, ArtifactService.Digizeni));
            if (pay > 0)
                ArtifactService.TrySpend(acc, ArtifactService.Digizeni, pay, out _);
            result.tookCurrency = pay > 0;
            result.digizeniTaken = pay;
            result.message = pay > 0
                ? $"Rare Hunter took {pay} Digizeni (no unused cards)."
                : "Rare Hunter found nothing to take.";
            ProgressionService.Persist(acc);
            return result;
        }
    }
}
