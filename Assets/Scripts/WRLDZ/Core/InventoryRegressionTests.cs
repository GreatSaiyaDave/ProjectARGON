using System;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>
    /// Artifact box + soul-card regressions. Same Check() style as TcgRegressionTests.
    /// Wired into <c>wrldz_tcg_tests</c>.
    /// </summary>
    public static class InventoryRegressionTests
    {
        public static string RunAll()
        {
            var sb = new StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok)
                {
                    pass++;
                    sb.AppendLine("PASS  " + name);
                }
                else
                {
                    fail++;
                    sb.AppendLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
                }
            }

            // ── Catalog ──
            {
                ArtifactCatalog.Invalidate();
                var file = ArtifactCatalog.Load();
                Check("Catalog: loads", file != null && file.defs != null && file.defs.Length > 0);
                Check("Catalog: ids unique", ArtifactCatalog.IdsUnique(out var dup), dup ?? "");
                Check("Catalog: Digizeni exists", ArtifactCatalog.Get(ArtifactService.Digizeni) != null);
                Check("Catalog: Duel-Coin exists", ArtifactCatalog.Get(ArtifactService.DuelCoin) != null);
                Check("Catalog: Tome exists", ArtifactCatalog.Get(ArtifactService.Tome) != null);
                foreach (var band in ErazFormat.BandIdsInOrder())
                {
                    var id = ArtifactService.ErazId(band);
                    Check("Catalog: ERAZ " + band, ArtifactCatalog.Get(id) != null, id);
                }

                Check("Catalog: SE LOB tablet", ArtifactCatalog.Get(ArtifactService.SetEnergyId("LOB")) != null);
            }

            // ── Migrate + spend ──
            {
                var p = PlayerProgress.DefaultNew();
                p.digizeni = 500;
                p.duelCoin = 0;
                p.setEnergy = 0;
                var inv = PlayerInventory.Empty();
                inv.hasBackpack = true;
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.EnsureValid();
                ArtifactService.MigrateFromLegacy(p, inv);
                Check("Migrate: Digizeni 500", ArtifactService.Qty(inv, ArtifactService.Digizeni) == 500);
                Check("Migrate: Duel-Coin card present at 0",
                    ArtifactService.Has(inv, ArtifactService.DuelCoin)
                    && ArtifactService.Qty(inv, ArtifactService.DuelCoin) == 0);

                var spent = ArtifactService.TrySpend(p, inv, ArtifactService.Digizeni, 50, out var err);
                Check("Spend: 50 Digizeni ok", spent, err);
                Check("Spend: qty 450", ArtifactService.Qty(inv, ArtifactService.Digizeni) == 450);
                Check("Spend: cache matches", p.digizeni == 450);

                var broke = ArtifactService.TrySpend(p, inv, ArtifactService.Digizeni, 9999, out _);
                Check("Spend: insufficient fails", !broke);
                Check("Spend: qty unchanged on fail", ArtifactService.Qty(inv, ArtifactService.Digizeni) == 450);
            }

            // ── ERAZ badge card ──
            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.hasBackpack = true;
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.EnsureValid();
                ArtifactService.MigrateFromLegacy(p, inv);
                Check("Badge: fresh has no Original card",
                    !ArtifactService.Has(inv, ArtifactService.ErazId(ErazFormat.Original)));
                ArtifactService.Grant(p, inv, ArtifactService.ErazId(ErazFormat.Original), 1);
                Check("Badge: grant Original instance",
                    ArtifactService.Has(inv, ArtifactService.ErazId(ErazFormat.Original)));
                Check("Badge: CSV cache contains original",
                    ErazProgress.HasBadge(p, ErazFormat.Original));
            }

            // ── Endless box ──
            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.hasBackpack = true;
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.EnsureValid();
                ArtifactService.MigrateFromLegacy(p, inv);
                ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId("LOB"), 100);
                Check("Endless: 100 SE on one tablet",
                    ArtifactService.Qty(inv, ArtifactService.SetEnergyId("LOB")) == 100);
                Check("Endless: total SE cache 100", ArtifactService.SetEnergyTotal(inv) == 100 && p.setEnergy == 100);
                Check("Wallet: first SE id is LOB",
                    ArtifactService.FirstSetEnergyId(inv) == ArtifactService.SetEnergyId("LOB"));

                var extras = new ArtifactInstance[120];
                for (var i = 0; i < extras.Length; i++)
                    extras[i] = new ArtifactInstance { defId = "story.key." + i, qty = 1 };
                inv.artifactDeckBox.instances = extras;
                inv.EnsureValid();
                Check("Endless: EnsureValid does not trim 120 instances",
                    inv.artifactDeckBox.instances != null && inv.artifactDeckBox.instances.Length == 120);
            }

            // ── Currencies never occupy backpack ──
            {
                var inv = PlayerInventory.Empty();
                inv.hasBackpack = true;
                inv.hasDigizeniHolder = true;
                inv.hasDuelCoinHolder = true;
                inv.hasSetEnergyHolder = true;
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true, atHome = false };
                inv.backpack = new BackpackState { capacityTier = 1, unlocked = true };
                inv.EnsureValid();
                var kinds = inv.backpack.items ?? Array.Empty<BackpackItem>();
                var currencyOnGrid = false;
                var boxOnGrid = false;
                foreach (var it in kinds)
                {
                    if (it == null) continue;
                    if (it.Kind is BackpackItemKind.CurrencyDigizeni or BackpackItemKind.CurrencyDuelCoin
                        or BackpackItemKind.CurrencySetEnergy)
                        currencyOnGrid = true;
                    if (it.Kind == BackpackItemKind.ArtifactDeckBox)
                        boxOnGrid = true;
                }

                Check("Pack: no currency pouches", !currencyOnGrid);
                Check("Pack: artifact box not a grid tile", !boxOnGrid);
            }

            // ── Soul cards: wall-clock ticks while "logged out" ──
            {
                var p = PlayerProgress.DefaultNew();
                var inv = FullTravelBoxes(out var commonId);
                inv.EnsureValid();
                var now = 1_700_000_000L;
                var ok = SoulCardService.TryReceiveTcgCard(p, inv, commonId, 1, atHome: false, nowUnix: now,
                    out var leftover, out var err);
                Check("Soul: receive with full boxes creates tile", ok && leftover == 0, err);
                var soul = FirstSoul(inv);
                Check("Soul: backpack item exists", soul != null);
                Check("Soul: 4h common timer",
                    soul != null && soul.expiresUnix == now + SoulCardService.DurationHours(0) * 3600L);

                var later = now + SoulCardService.DurationHours(0) * 3600L + 1;
                var gone = SoulCardService.Tick(inv, later, paused: false);
                Check("Soul: logout/offline Tick expires copy", gone >= 1);
                Check("Soul: expired tile removed", FirstSoul(inv) == null);
                Check("Soul: copy not in storage", inv.CountOf(commonId) == 0);
            }

            {
                var p = PlayerProgress.DefaultNew();
                var inv = FullTravelBoxes(out var commonId);
                inv.EnsureValid();
                var now = 1_700_000_000L;
                SoulCardService.TryReceiveTcgCard(p, inv, commonId, 1, atHome: false, nowUnix: now, out _, out _);
                var later = now + SoulCardService.DurationHours(0) * 3600L + 1;
                var gone = SoulCardService.Tick(inv, later, paused: true);
                Check("Soul: paused Tick does not expire", gone == 0 && FirstSoul(inv) != null);
            }

            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.hasBackpack = true;
                inv.storageBoxes = new[]
                {
                    new StorageBoxState
                    {
                        name = "Travel",
                        capacity = 100,
                        atHome = false,
                        stacks = Array.Empty<CardStackEntry>()
                    }
                };
                inv.backpack = new BackpackState { capacityTier = 1, unlocked = true };
                inv.EnsureValid();
                var now = 1_700_000_000L;
                SoulCardService.TryReceiveTcgCard(p, inv, 46986414, 1, atHome: false, nowUnix: now, out _, out _);
                Check("Soul: free carried box files, no soul tile",
                    FirstSoul(inv) == null && inv.CountOf(46986414) == 1);
            }

            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.hasBackpack = true;
                inv.storageBoxes = new[]
                {
                    new StorageBoxState
                    {
                        name = "Home full",
                        capacity = PlayerInventory.StorageBoxSizeSmall,
                        atHome = true,
                        stacks = new[]
                        {
                            new CardStackEntry { cardId = 1, qty = PlayerInventory.StorageBoxSizeSmall }
                        }
                    }
                };
                inv.backpack = new BackpackState { capacityTier = 1, unlocked = true };
                inv.EnsureValid();
                var now = 1_700_000_000L;
                SoulCardService.TryReceiveTcgCard(p, inv, 46986414, 1, atHome: true, nowUnix: now, out _, out _);
                var soul = FirstSoul(inv);
                Check("Soul: home full freezes timer", soul != null && soul.expiresUnix == 0);
                SoulCardService.Tick(inv, now + 99_999, paused: false);
                Check("Soul: frozen does not expire at home", FirstSoul(inv) != null);
            }

            sb.AppendLine($"Inventory regression: {pass} pass, {fail} fail");
            return sb.ToString();
        }

        static PlayerInventory FullTravelBoxes(out int cardId)
        {
            cardId = 46986414; // Dark Magician — common-band default
            var inv = PlayerInventory.Empty();
            inv.hasBackpack = true;
            inv.storageBoxes = new[]
            {
                new StorageBoxState
                {
                    name = "Full travel",
                    capacity = PlayerInventory.StorageBoxSizeSmall,
                    atHome = false,
                    stacks = new[] { new CardStackEntry { cardId = 99, qty = PlayerInventory.StorageBoxSizeSmall } }
                }
            };
            inv.backpack = new BackpackState { capacityTier = 1, unlocked = true };
            return inv;
        }

        static BackpackItem FirstSoul(PlayerInventory inv)
        {
            if (inv?.backpack?.items == null) return null;
            foreach (var it in inv.backpack.items)
                if (it != null && it.Kind == BackpackItemKind.SoulCard)
                    return it;
            return null;
        }
    }
}
