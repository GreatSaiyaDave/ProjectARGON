using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>
    /// Source of truth for artifact cards in the endless Artifact Deck Box.
    /// Progress currency ints / ERAZ CSV are derived caches.
    /// </summary>
    public static class ArtifactService
    {
        public const string Digizeni = "currency.digizeni";
        public const string DuelCoin = "currency.duel_coin";
        public const string Tome = "key.tome";
        public const string TradeTransport = "transport.trade";

        public static string ErazId(string bandId) =>
            string.IsNullOrEmpty(bandId) ? "" : "eraz." + bandId.Trim();

        public static string SetEnergyId(string setCode) =>
            string.IsNullOrEmpty(setCode) ? "" : "se." + setCode.Trim().ToUpperInvariant();

        public static void Grant(LocalAccountStore.Account acc, string defId, int qty)
        {
            if (acc == null) return;
            acc.EnsureProgress();
            acc.EnsureInventory();
            Grant(acc.progress, acc.inventory, defId, qty);
        }

        public static bool TrySpend(LocalAccountStore.Account acc, string defId, int qty, out string error)
        {
            error = "No account.";
            if (acc == null) return false;
            acc.EnsureProgress();
            acc.EnsureInventory();
            return TrySpend(acc.progress, acc.inventory, defId, qty, out error);
        }

        public static void Grant(PlayerProgress p, PlayerInventory inv, string defId, int qty)
        {
            if (p == null || inv == null || string.IsNullOrEmpty(defId) || qty < 0) return;
            EnsureBox(inv);
            var def = ArtifactCatalog.Get(defId);
            var stackable = def == null || def.stackable;
            var inst = Find(inv, defId);
            if (inst == null)
            {
                if (qty == 0 && stackable && IsWalletAlwaysPresent(defId))
                {
                    AddNew(inv, defId, 0, DefaultCharges(def));
                    SyncCaches(p, inv);
                    return;
                }

                if (qty <= 0) return;
                AddNew(inv, defId, stackable ? qty : 1, DefaultCharges(def));
                SyncCaches(p, inv);
                return;
            }

            if (!stackable)
            {
                inst.qty = Mathf.Max(1, inst.qty);
                SyncCaches(p, inv);
                return;
            }

            inst.qty = Mathf.Max(0, inst.qty + qty);
            SyncCaches(p, inv);
        }

        public static bool TrySpend(PlayerProgress p, PlayerInventory inv, string defId, int qty, out string error)
        {
            error = null;
            if (p == null || inv == null)
            {
                error = "No inventory.";
                return false;
            }

            if (qty <= 0) return true;
            EnsureBox(inv);
            var have = Qty(inv, defId);
            if (have < qty)
            {
                var def = ArtifactCatalog.Get(defId);
                var name = def != null && !string.IsNullOrEmpty(def.name) ? def.name : defId;
                error = $"Need {qty} {name} (have {have}).";
                return false;
            }

            var inst = Find(inv, defId);
            if (inst == null)
            {
                error = $"Need {qty} {defId} (have 0).";
                return false;
            }

            inst.qty -= qty;
            if (inst.qty <= 0 && !IsWalletAlwaysPresent(defId))
                Remove(inv, defId);
            else if (inst.qty < 0)
                inst.qty = 0;

            SyncCaches(p, inv);
            return true;
        }

        public static int Qty(PlayerInventory inv, string defId)
        {
            var inst = Find(inv, defId);
            return inst == null ? 0 : Mathf.Max(0, inst.qty);
        }

        public static bool Has(PlayerInventory inv, string defId)
        {
            var inst = Find(inv, defId);
            if (inst == null) return false;
            var def = ArtifactCatalog.Get(defId);
            if (def != null && !def.stackable) return true;
            return inst.qty > 0 || IsWalletAlwaysPresent(defId);
        }

        public static int SetEnergyOf(PlayerInventory inv, string setCode) =>
            Qty(inv, SetEnergyId(setCode));

        public static int SetEnergyTotal(PlayerInventory inv)
        {
            if (inv?.artifactDeckBox?.instances == null) return 0;
            var n = 0;
            foreach (var inst in inv.artifactDeckBox.instances)
            {
                if (inst == null || string.IsNullOrEmpty(inst.defId)) continue;
                if (!inst.defId.StartsWith("se.", StringComparison.OrdinalIgnoreCase)) continue;
                n += Mathf.Max(0, inst.qty);
            }

            return n;
        }

        /// <summary>
        /// First owned Set Energy tablet id, else <c>null</c>.
        /// Wallet ⚡ uses this so focus is not hardcoded to LOB.
        /// </summary>
        public static string FirstSetEnergyId(PlayerInventory inv)
        {
            if (inv?.artifactDeckBox?.instances == null) return null;
            foreach (var inst in inv.artifactDeckBox.instances)
            {
                if (inst == null || string.IsNullOrEmpty(inst.defId)) continue;
                if (!inst.defId.StartsWith("se.", StringComparison.OrdinalIgnoreCase)) continue;
                if (inst.qty > 0) return inst.defId;
            }

            return null;
        }

        /// <summary>Focus token that opens the TABLET filter when no SE tablet is owned.</summary>
        public const string SetEnergyFocus = "se.";

        public static void GrantUntaggedSetEnergy(PlayerProgress p, PlayerInventory inv, int qty)
        {
            if (qty <= 0) return;
            var set = FirstUnlockedSet(p);
            Grant(p, inv, SetEnergyId(set), qty);
        }

        public static void SetQty(PlayerProgress p, PlayerInventory inv, string defId, int qty)
        {
            if (p == null || inv == null || string.IsNullOrEmpty(defId)) return;
            EnsureBox(inv);
            qty = Mathf.Max(0, qty);
            var inst = Find(inv, defId);
            if (inst == null)
            {
                if (qty == 0 && !IsWalletAlwaysPresent(defId)) return;
                var def = ArtifactCatalog.Get(defId);
                AddNew(inv, defId, qty, DefaultCharges(def));
            }
            else
            {
                inst.qty = qty;
            }

            SyncCaches(p, inv);
        }

        public static void MigrateFromLegacy(PlayerProgress p, PlayerInventory inv)
        {
            if (p == null || inv == null) return;
            EnsureBox(inv);
            p.EnsureValid();
            inv.EnsureValid();

            if (!Has(inv, Digizeni) || Qty(inv, Digizeni) == 0 && p.digizeni > 0)
            {
                if (Find(inv, Digizeni) == null)
                    AddNew(inv, Digizeni, Mathf.Max(0, p.digizeni), 0);
                else if (Qty(inv, Digizeni) == 0 && p.digizeni > 0)
                    Find(inv, Digizeni).qty = p.digizeni;
            }

            if (Find(inv, DuelCoin) == null)
                AddNew(inv, DuelCoin, Mathf.Max(0, p.duelCoin), 0);
            else if (Qty(inv, DuelCoin) == 0 && p.duelCoin > 0)
                Find(inv, DuelCoin).qty = p.duelCoin;

            if (SetEnergyTotal(inv) == 0 && p.setEnergy > 0)
                Grant(p, inv, SetEnergyId(FirstUnlockedSet(p)), p.setEnergy);

            var badges = ErazProgress.Owned(p);
            for (var i = 0; i < badges.Length; i++)
            {
                var id = ErazId(badges[i]);
                if (!string.IsNullOrEmpty(id) && Find(inv, id) == null)
                    AddNew(inv, id, 1, 0);
            }

            if (inv.tome != null && inv.tome.hasTomeItem && Find(inv, Tome) == null)
                AddNew(inv, Tome, 1, 0);

            if (inv.tradeTransportCharges > 0 && Find(inv, TradeTransport) == null)
                AddNew(inv, TradeTransport, inv.tradeTransportCharges, 0);

            // Always-present wallet
            if (Find(inv, Digizeni) == null) AddNew(inv, Digizeni, 0, 0);
            if (Find(inv, DuelCoin) == null) AddNew(inv, DuelCoin, 0, 0);

            SyncCaches(p, inv);
        }

        public static void SyncCaches(PlayerProgress p, PlayerInventory inv)
        {
            if (p == null || inv == null) return;
            EnsureBox(inv);
            p.digizeni = Mathf.Max(0, Qty(inv, Digizeni));
            p.duelCoin = Mathf.Max(0, Qty(inv, DuelCoin));
            p.setEnergy = Mathf.Max(0, SetEnergyTotal(inv));

            var badges = new List<string>();
            if (inv.artifactDeckBox.instances != null)
            {
                foreach (var inst in inv.artifactDeckBox.instances)
                {
                    if (inst == null || string.IsNullOrEmpty(inst.defId)) continue;
                    if (!inst.defId.StartsWith("eraz.", StringComparison.OrdinalIgnoreCase)) continue;
                    badges.Add(inst.defId.Substring("eraz.".Length));
                }
            }

            p.erazBadgesCsv = string.Join(",", badges);

            var transport = Find(inv, TradeTransport);
            inv.tradeTransportCharges = transport == null ? 0 : Mathf.Max(0, transport.qty);
        }

        static bool IsWalletAlwaysPresent(string defId) =>
            string.Equals(defId, Digizeni, StringComparison.OrdinalIgnoreCase)
            || string.Equals(defId, DuelCoin, StringComparison.OrdinalIgnoreCase);

        static int DefaultCharges(ArtifactDef def) =>
            def != null && def.Kind == ArtifactKind.Millennium ? 3 : 0;

        static string FirstUnlockedSet(PlayerProgress p)
        {
            if (p == null || string.IsNullOrEmpty(p.unlockedSetsCsv)) return "LOB";
            var parts = p.unlockedSetsCsv.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var s = parts[i].Trim();
                if (s.Length > 0) return s.ToUpperInvariant();
            }

            return "LOB";
        }

        static void EnsureBox(PlayerInventory inv)
        {
            inv.artifactDeckBox ??= new ArtifactDeckBoxState { owned = true };
            inv.artifactDeckBox.owned = true;
            inv.artifactDeckBox.instances ??= Array.Empty<ArtifactInstance>();
        }

        static ArtifactInstance Find(PlayerInventory inv, string defId)
        {
            if (inv?.artifactDeckBox?.instances == null || string.IsNullOrEmpty(defId)) return null;
            foreach (var inst in inv.artifactDeckBox.instances)
            {
                if (inst != null && string.Equals(inst.defId, defId, StringComparison.OrdinalIgnoreCase))
                    return inst;
            }

            return null;
        }

        static void AddNew(PlayerInventory inv, string defId, int qty, int charges)
        {
            var list = new List<ArtifactInstance>(inv.artifactDeckBox.instances ?? Array.Empty<ArtifactInstance>());
            list.Add(new ArtifactInstance
            {
                defId = defId,
                qty = Mathf.Max(0, qty),
                charges = Mathf.Max(0, charges),
                acquiredUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            inv.artifactDeckBox.instances = list.ToArray();
        }

        static void Remove(PlayerInventory inv, string defId)
        {
            if (inv?.artifactDeckBox?.instances == null) return;
            var list = new List<ArtifactInstance>();
            foreach (var inst in inv.artifactDeckBox.instances)
            {
                if (inst == null) continue;
                if (string.Equals(inst.defId, defId, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(inst);
            }

            inv.artifactDeckBox.instances = list.ToArray();
        }
    }
}
