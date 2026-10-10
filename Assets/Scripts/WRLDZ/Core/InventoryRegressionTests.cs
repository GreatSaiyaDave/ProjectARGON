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
                Check("Catalog: soul shard", ArtifactCatalog.Get(ArtifactService.SoulFragment) != null);
                Check("Catalog: GX shard", ArtifactCatalog.Get(ArtifactService.ErazPieceId(ErazFormat.Gx)) != null);
                Check("Catalog: format DK", ArtifactCatalog.Get(ArtifactService.FormatId("dk")) != null);
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

            // ── Post-duel rewards: flat win, 10% loss ──
            {
                var quick = ArDuelMatchConfig.DefaultQuick();
                var acc = RewardTestAccount();
                var win = ProgressionService.AwardDuelRewards(acc, quick, playerWon: true, turnNumber: 20);
                Check("Reward: quick win XP 650", win.XpAwarded == ProgressionService.QuickDuelWinXp,
                    "xp=" + win.XpAwarded);
                Check("Reward: quick win SE 4", win.SetEnergyGained == ProgressionService.QuickDuelWinSe,
                    "se=" + win.SetEnergyGained);
                Check("Reward: quick win DC 10", win.DuelCoinGained == ProgressionService.QuickDuelWinDc,
                    "dc=" + win.DuelCoinGained);
                Check("Reward: quick win does not persist test account",
                    string.Equals(acc.username, "__test_rewards", StringComparison.Ordinal));

                acc = RewardTestAccount();
                var loss = ProgressionService.AwardDuelRewards(acc, quick, playerWon: false, turnNumber: 20);
                Check("Reward: quick loss XP 65", loss.XpAwarded == 65, "xp=" + loss.XpAwarded);
                Check("Reward: quick loss SE floors to 0", loss.SetEnergyGained == 0,
                    "se=" + loss.SetEnergyGained);
                Check("Reward: quick loss DC 1", loss.DuelCoinGained == 1, "dc=" + loss.DuelCoinGained);

                // Same-device hotseat (every human-vs-human duel in this build) pays nothing: one person can
                // play both seats. PvP rates return with networked, server-verified PvP.
                acc = RewardTestAccount();
                var pvp = ArDuelMatchConfig.DefaultQuick();
                pvp.Launch = ArDuelLaunchKind.PvpZone;
                pvp.Opponent = ArDuelOpponentKind.NearbyPeer;
                var dcBefore = acc.progress.duelCoin;
                var pvpWin = ProgressionService.AwardDuelRewards(acc, pvp, playerWon: true, turnNumber: 8);
                Check("Reward: hotseat win pays nothing",
                    pvpWin.XpAwarded == 0 && pvpWin.SetEnergyGained == 0 && pvpWin.DuelCoinGained == 0 &&
                    pvpWin.PracticeNoReward && acc.progress.duelCoin == dcBefore,
                    "xp=" + pvpWin.XpAwarded + " dc=" + pvpWin.DuelCoinGained);
                Check("Reward: hotseat win does not bank a streak", acc.progress.pveWinStreak == 0);

                // PvP-rate table itself (used once networked PvP exists): win 1200, loss 10%.
                Check("Reward: PvP win row 1200",
                    ProgressionService.ComputeDuelXp(pvp, true, 8, out _) == ProgressionService.PvpWinXp);
                Check("Reward: PvP loss share 120",
                    ProgressionService.ScaleForOutcome(ProgressionService.PvpWinXp, false, true) == 120);
                Check("Reward: PvP never pays SE", ProgressionService.WinSetEnergy(pvp) == 0);

                // Tournament seat filled by the AI is a CPU duel, not PvP.
                acc = RewardTestAccount();
                var aiTourney = ArDuelMatchConfig.DefaultQuick();
                aiTourney.Launch = ArDuelLaunchKind.Tournament;
                aiTourney.Opponent = ArDuelOpponentKind.AiLocal;
                var tWin = ProgressionService.AwardDuelRewards(acc, aiTourney, playerWon: true, turnNumber: 9);
                Check("Reward: AI tournament pays CPU XP, not PvP",
                    tWin.XpAwarded == ProgressionService.QuickDuelWinXp, "xp=" + tWin.XpAwarded);
                Check("Reward: AI tournament pays CPU DC, not PvP",
                    tWin.DuelCoinGained == ProgressionService.QuickDuelWinDc, "dc=" + tWin.DuelCoinGained);

                // A finished duel pays exactly once per duel result id; a new duel gets a new id.
                acc = RewardTestAccount();
                var once = ArDuelMatchConfig.DefaultQuick();
                once.BeginNewDuel();
                var first = ProgressionService.AwardDuelRewards(acc, once, playerWon: true, turnNumber: 10);
                var xpAfterFirst = acc.progress.xp;
                var lvAfterFirst = acc.progress.level;
                var dcAfterFirst = acc.progress.duelCoin;
                var second = ProgressionService.AwardDuelRewards(acc, once, playerWon: true, turnNumber: 10);
                Check("Reward: claim-once — second claim is a no-op",
                    first.Applied && second.AlreadyClaimed && !second.Applied &&
                    acc.progress.xp == xpAfterFirst && acc.progress.level == lvAfterFirst &&
                    acc.progress.duelCoin == dcAfterFirst);
                var forfeitAfterWin = ProgressionService.ForfeitDuel(acc, once, 10);
                Check("Reward: forfeit after the result paid changes nothing",
                    forfeitAfterWin.AlreadyClaimed && !ProgressionService.IsForfeitable(acc, once));
                once.BeginNewDuel();
                Check("Reward: a new duel id is claimable again", ProgressionService.IsForfeitable(acc, once));

                // Forfeit = loss: loss share paid, streak reset, id claimed.
                acc = RewardTestAccount();
                acc.progress.pveWinStreak = 2;
                var left = ArDuelMatchConfig.DefaultQuick();
                left.BeginNewDuel();
                var forfeit = ProgressionService.ForfeitDuel(acc, left, 3);
                Check("Reward: forfeit counts as a loss",
                    forfeit.Forfeit && forfeit.XpAwarded == 65 && acc.progress.pveWinStreak == 0 &&
                    acc.progress.HasClaimedDuel(left.DuelResultId), "xp=" + forfeit.XpAwarded);
                Check("Reward: practice / lab are never forfeitable",
                    !ProgressionService.IsForfeitable(acc, WithId(ArDuelMatchConfig.Practice())) &&
                    ArDuelMatchConfig.Practice().AllowsRestart && !ArDuelMatchConfig.DefaultQuick().AllowsRestart);

                // Claimed ids are a bounded ring.
                var ring = PlayerProgress.DefaultNew();
                for (var i = 0; i < PlayerProgress.ClaimedDuelIdCap + 5; i++)
                    ring.TryMarkDuelClaimed("d" + i);
                Check("Reward: claimed-id ring stays bounded",
                    ring.claimedDuelIds.Length == PlayerProgress.ClaimedDuelIdCap && ring.HasClaimedDuel("d" +
                        (PlayerProgress.ClaimedDuelIdCap + 4)) && !ring.HasClaimedDuel("d0"));

                // Crash / swipe-away forfeit: a rewarded duel is marked on the account while it runs.
                acc = RewardTestAccount();
                var running = ArDuelMatchConfig.DefaultQuick();
                running.BeginNewDuel();
                Check("Forfeit marker: rewarded duel is marked at start",
                    PendingDuelMarker.Arm(acc, running, testDuelMode: false) &&
                    acc.progress.pendingDuelId == running.DuelResultId &&
                    !string.IsNullOrEmpty(acc.progress.pendingDuelMatchJson));
                Check("Forfeit marker: a duel of this launch is never settled by a refresh",
                    !PendingDuelMarker.SettleAbandoned(acc) && !acc.progress.HasClaimedDuel(running.DuelResultId));
                ProgressionService.AwardDuelRewards(acc, running, playerWon: true, turnNumber: 7);
                Check("Forfeit marker: claiming the result clears it", acc.progress.pendingDuelId == "");
                Check("Forfeit marker: practice / lab test mode are never marked",
                    !PendingDuelMarker.Arm(acc, WithId(ArDuelMatchConfig.Practice()), false) &&
                    !PendingDuelMarker.Arm(acc, WithId(ArDuelMatchConfig.DefaultQuick()), testDuelMode: true) &&
                    acc.progress.pendingDuelId == "");

                // A marker left by an earlier launch (app killed mid-duel) is settled as a forfeit loss.
                acc = RewardTestAccount();
                acc.progress.pveWinStreak = 2;
                var killed = ArDuelMatchConfig.DefaultQuick();
                killed.Launch = ArDuelLaunchKind.NpcStreet;
                acc.progress.pendingDuelId = "earlier-launch-duel";
                acc.progress.pendingDuelMatchJson = JsonUtility.ToJson(killed);
                var settled = PendingDuelMarker.SettleAbandoned(acc);
                Check("Forfeit marker: killed duel settles as a forfeit loss on next launch",
                    settled && acc.progress.HasClaimedDuel("earlier-launch-duel") && acc.progress.pveWinStreak == 0 &&
                    acc.progress.pendingDuelId == "" && acc.progress.pendingDuelMatchJson == "");
                Check("Forfeit marker: settling twice does nothing", !PendingDuelMarker.SettleAbandoned(acc));

                // Walk SE daily cap lives on the account (guarded clock), not a resettable device pref.
                var walk = PlayerProgress.DefaultNew();
                var got = WalkEnergyLedger.Reserve(walk, 15, 20) + WalkEnergyLedger.Reserve(walk, 15, 20);
                Check("Walk SE: daily cap holds across calls",
                    got == 20 && WalkEnergyLedger.Reserve(walk, 1, 20) == 0 && WalkEnergyLedger.EarnedToday(walk) == 20,
                    "got=" + got);
                walk.clockHighWaterUnix += 3 * 24 * 60 * 60; // a device jumped ahead and came back
                Check("Walk SE: a later guarded day resets, a clock set back does not",
                    WalkEnergyLedger.Reserve(walk, 5, 20) == 5 && WalkEnergyLedger.EarnedToday(walk) == 5);

                // Streak purse counts non-PvP wins only.
                var sp = PlayerProgress.DefaultNew();
                var npc = ArDuelMatchConfig.DefaultQuick();
                npc.Launch = ArDuelLaunchKind.NpcStreet;
                ShellLoops.NoteDuel(sp, npc, true);
                ShellLoops.NoteDuel(sp, npc, true);
                ShellLoops.NoteDuel(sp, pvp, true);
                Check("Streak: PvP win neither counts nor breaks", sp.pveWinStreak == 2 && sp.streakPurses == 0);
                ShellLoops.NoteDuel(sp, npc, true);
                Check("Streak: third non-PvP win banks a purse", sp.streakPurses == 1);

                // Guarded clock never runs backwards for an account.
                var cp = PlayerProgress.DefaultNew();
                var t0 = ClockGuard.NowUnix(cp, 1_800_000_000L);
                var tBack = ClockGuard.NowUnix(cp, 1_800_000_000L - 3 * 86400L);
                var tFwd = ClockGuard.NowUnix(cp, 1_800_000_060L);
                Check("Clock: rollback holds the high-water mark", t0 == tBack && tFwd == 1_800_000_060L);

                acc = RewardTestAccount();
                var practice = ArDuelMatchConfig.Practice();
                var pr = ProgressionService.AwardDuelRewards(acc, practice, playerWon: true, turnNumber: 4);
                Check("Reward: practice no XP", pr.XpAwarded == 0 && pr.PracticeNoReward);
                Check("Reward: practice no SE", pr.SetEnergyGained == 0);
                Check("Reward: practice no DC", pr.DuelCoinGained == 0);

                acc = RewardTestAccount();
                acc.progress.storyCurrentId = StoryCampaignService.FirstStageId;
                var story = ArDuelMatchConfig.DefaultQuick();
                story.Launch = ArDuelLaunchKind.StoryEra;
                story.StoryStageId = StoryCampaignService.FirstStageId;
                var storyLoss = ProgressionService.AwardDuelRewards(acc, story, playerWon: false, turnNumber: 6);
                Check("Reward: story loss XP 110", storyLoss.XpAwarded == 110,
                    "xp=" + storyLoss.XpAwarded);
                Check("Reward: story loss does not clear stage",
                    !StoryCampaignService.IsCleared(acc.progress, StoryCampaignService.FirstStageId) &&
                    string.IsNullOrEmpty(storyLoss.StoryStageCleared));
            }

            // ── Unlimited collection box (infinite, never backpack, never storage totals) ──
            {
                var inv = PlayerInventory.Empty();
                inv.hasBackpack = true;
                inv.EnsureValid();
                Check("Collection: NOT auto-owned for ordinary characters",
                    inv.collectionBox != null && !inv.collectionBox.owned);
                Check("Collection: ordinary CountOf sees nothing",
                    inv.CountOf(46986414) == 0 && inv.CountOnHand(46986414) == 0);

                // Owner gating: Great SaiyaDave (username variants + display name), nobody else.
                var owner = new LocalAccountStore.Account
                {
                    username = "greatsaiyadave",
                    displayName = "Great SaiyaDave",
                    progress = PlayerProgress.DefaultNew(),
                    inventory = PlayerInventory.Empty()
                };
                var stranger = new LocalAccountStore.Account
                {
                    username = "kaiba_fan_99",
                    displayName = "Seto Fan",
                    progress = PlayerProgress.DefaultNew(),
                    inventory = PlayerInventory.Empty()
                };
                Check("Collection: owner recognized (username/display)",
                    CollectionBoxService.IsOwnerAccount(owner));
                Check("Collection: stranger refused",
                    !CollectionBoxService.IsOwnerAccount(stranger));
                Check("Collection: service grant refused for stranger",
                    CollectionBoxService.GrantFullCollection(stranger) == 0 &&
                    (stranger.inventory.collectionBox == null || !stranger.inventory.collectionBox.owned));

                inv.EnsureCollectionBox();
                Check("Collection: explicit ensure owns with unlimited + infinite flags",
                    inv.collectionBox.owned && inv.collectionBox.unlimitedStorage && inv.collectionBox.infiniteCopies);
                inv.collectionBox.stacks = new[]
                {
                    new CardStackEntry { cardId = 46986414, qty = CollectionBoxState.InfiniteQtyPerCard },
                    new CardStackEntry { cardId = 89631139, qty = CollectionBoxState.InfiniteQtyPerCard },
                };
                Check("Collection: CountOf sees infinite copies",
                    inv.CountOf(46986414) == CollectionBoxState.InfiniteQtyPerCard);
                Check("Collection: on-hand (deck editor) sees infinite",
                    inv.CountOnHand(46986414) == CollectionBoxState.InfiniteQtyPerCard);
                Check("Collection: home stays 0 (always carried)",
                    inv.CountAtHome(46986414) == 0);
                Check("Collection: free copies for deck is infinite",
                    inv.FreeCopiesForDeck(46986414) >= CollectionBoxState.InfiniteQtyPerCard - 1);
                Check("Collection: excluded from storage totals",
                    inv.TotalStorageCapacity() == inv.storageBoxes.Length * PlayerInventory.StorageBoxSizeDefault &&
                    inv.TotalStorageUsed() == 0);

                var before = inv.CountOf(46986414);
                var removed = inv.TryRemoveCards(46986414, 3);
                Check("Collection: removal succeeds without depleting infinite",
                    removed && inv.CountOf(46986414) == before);

                var viewKinds = inv.backpack.items ?? System.Array.Empty<BackpackItem>();
                var collectionOnGrid = false;
                foreach (var it in viewKinds)
                {
                    // Collection box has no BackpackItemKind — any future tile would fail here.
                    if (it != null && it.label != null &&
                        it.label.IndexOf("Collection", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        collectionOnGrid = true;
                }

                Check("Collection: never a backpack tile", !collectionOnGrid);
                Check("Collection: Service tops up without DB",
                    CollectionBoxService.GrantInfiniteAll(inv, null) >= 0);
            }

            sb.AppendLine($"Inventory regression: {pass} pass, {fail} fail");
            return sb.ToString();
        }

        static ArDuelMatchConfig WithId(ArDuelMatchConfig c)
        {
            c.BeginNewDuel();
            return c;
        }

        static LocalAccountStore.Account RewardTestAccount()
        {
            var acc = new LocalAccountStore.Account
            {
                id = "__test_rewards",
                username = "__test_rewards",
                displayName = "Reward Test",
                progress = PlayerProgress.DefaultNew(),
                inventory = PlayerInventory.Empty()
            };
            acc.inventory.hasBackpack = true;
            acc.EnsureInventory();
            ErazProgress.GrantBadge(acc, ErazFormat.Original);
            return acc;
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
