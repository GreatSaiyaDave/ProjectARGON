using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>Story campaign, ERAZ shards, tablets, XP plateau. Wired into wrldz_tcg_tests.</summary>
    public static class StoryCampaignTests
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

            StoryCampaignService.Invalidate();
            ArtifactCatalog.Invalidate();

            {
                var file = StoryCampaignService.Load();
                Check("Story catalog loads", file != null && file.stages != null && file.stages.Length >= 10);
                Check("Story first is Weevil", StoryCampaignService.Stage("s1_weevil") != null
                    && StoryCampaignService.Stage("s1_weevil").aiDeckFile == "character_dk_weevil.json");
                Check("Story last is Pegasus", StoryCampaignService.Stage("s1_pegasus") != null);
                Check("Story S1 is Original ERAZ", file.erazBandId == ErazFormat.Original);
                Check("Story Weevil no format overlay", !StoryCampaignService.Stage("s1_weevil").dkOverlay);
                Check("Story Weevil 8000 LP", StoryCampaignService.Stage("s1_weevil").startingLp == 8000);
            }

            {
                var p = PlayerProgress.DefaultNew();
                StoryCampaignService.Ensure(p);
                Check("Current starts at Weevil", p.storyCurrentId == "s1_weevil");
                Check("Rex locked", StoryCampaignService.IsLocked(p, "s1_rex"));
                Check("Cannot skip to Pegasus", !StoryCampaignService.TryComplete(p, "s1_pegasus"));
                Check("Complete Weevil", StoryCampaignService.TryComplete(p, "s1_weevil"));
                Check("Current is Rex", p.storyCurrentId == "s1_rex");
                Check("Replay Weevil does not re-complete", !StoryCampaignService.TryComplete(p, "s1_weevil"));
                Check("Weevil still playable as replay", StoryCampaignService.CanPlay(p, "s1_weevil"));
            }

            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.storageBoxes = new[]
                {
                    new StorageBoxState { name = "Home", capacity = 1000, atHome = true }
                };
                inv.EnsureValid();
                ErazProgress.GrantTutorialBadge(p);
                ArtifactService.Grant(p, inv, ArtifactService.ErazId(ErazFormat.Original), 1);
                var stage = StoryCampaignService.Stage("s1_weevil");
                var line = StoryCampaignService.GrantFirstClearRewards(p, inv, stage, out var se);
                Check("First clear grants SE", se > 0 && ArtifactService.SetEnergyOf(inv, "LOB") == se, line);
                Check("First clear grants soul shard", ArtifactService.Qty(inv, ArtifactService.SoulFragment) >= 1);
                Check("First clear grants reward card", inv.CountOf(stage.rewardCardId) >= 1);
            }

            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.EnsureValid();
                ArtifactService.Grant(p, inv, ArtifactService.SoulFragment, 5);
                Check("Five soul shards fuse to +1 capacity",
                    p.soulFractureCapacity == PlayerProgress.DefaultSoulFractureCapacity + 1
                    && ArtifactService.Qty(inv, ArtifactService.SoulFragment) == 0);
            }

            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.EnsureValid();
                ArtifactService.Grant(p, inv, ArtifactService.ErazPieceId(ErazFormat.Gx), 1);
                Check("GX shard is not a whole badge",
                    !ErazProgress.HasBadge(p, ErazFormat.Gx)
                    && ArtifactService.Qty(inv, ArtifactService.ErazPieceId(ErazFormat.Gx)) == 1);
                Check("Cannot earn SRL SE without Original badge",
                    !ArtifactService.CanEarnSetEnergy(p, "SRL"));
                ErazProgress.GrantTutorialBadge(p);
                Check("Can earn LOB SE with Original", ArtifactService.CanEarnSetEnergy(p, "LOB"));
            }

            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.EnsureValid();
                ErazProgress.GrantTutorialBadge(p);
                ArtifactService.Grant(p, inv, ArtifactService.ErazId(ErazFormat.Original), 1);
                ArtifactService.Grant(p, inv, ArtifactService.ErazPieceId(ErazFormat.Gx), 5);
                ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId("LOB"), 2500);
                var merged = ErazMergeService.TryMerge(p, inv, ErazFormat.Gx, out var err);
                Check("Fuse GX badge from 5 shards + SE", merged && ErazProgress.HasBadge(p, ErazFormat.Gx), err);
                Check("Shards spent", ArtifactService.Qty(inv, ArtifactService.ErazPieceId(ErazFormat.Gx)) == 0);
            }

            {
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.storageBoxes = new[]
                {
                    new StorageBoxState { name = "Home", capacity = 1000, atHome = true }
                };
                inv.EnsureValid();
                ErazProgress.GrantTutorialBadge(p);
                ArtifactService.Grant(p, inv, ArtifactService.ErazId(ErazFormat.Original), 1);
                ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId("LOB"), 1000);
                var opened = StoneTabletService.TryOpen(p, inv, "LOB", out var ids, out var err);
                Check("Tablet opens 10 cards", opened && ids != null && ids.Length == 10, err);
                Check("Tablet spent 1000 SE", ArtifactService.SetEnergyOf(inv, "LOB") == 0);
            }

            {
                Check("Plateau PVE −90% at 50",
                    ProgressionService.ApplyMasteryPlateau(900, 50, pvp: false) == 90);
                Check("Plateau PvP +100% at 50",
                    ProgressionService.ApplyMasteryPlateau(1200, 50, pvp: true) == 2400);
                Check("Plateau off before 50",
                    ProgressionService.ApplyMasteryPlateau(900, 49, pvp: false) == 900);
                Check("XP 1–50 GO-early: L20 << L50 wall",
                    DuelistXpCurve.XpToNextLevel(20) * 5 < DuelistXpCurve.XpToNextLevel(50));
            }

            {
                var engine = new DuelEngine();
                engine.Overlay = DuelRulesOverlay.DuelistKingdomTable();
                Check("DK overlay forbids directs", !engine.CanAttackDirectly(null, null));
            }

            {
                var p = PlayerProgress.DefaultNew();
                var q = QuestService.Ensure(p);
                Check("Three dailies", q != null && q.slots != null && q.slots.Length == 3);
                var match = new ArDuelMatchConfig { Launch = ArDuelLaunchKind.StoryEra };
                QuestService.CreditDuel(p, match, playerWon: true);
                q = QuestService.Ensure(p);
                var play = false;
                foreach (var s in q.slots)
                    if (s != null && s.id == QuestService.PlayStory)
                        play = s.progress >= 1;
                Check("Story duel credits daily", play);
            }

            {
                OpponentCatalog.Invalidate();
                var roster = OpponentCatalog.All();
                Check("Opponent roster loads", roster != null && roster.Count >= 14);
                var banned = false;
                var kaiba = false;
                var filesOk = true;
                for (var i = 0; i < roster.Count; i++)
                {
                    var o = roster[i];
                    if (OpponentCatalog.IsBanned(o.file)) banned = true;
                    if (o.id == "dk_kaiba") kaiba = true;
                    if (!OpponentCatalog.DeckFileExists(o.file)) filesOk = false;
                }

                Check("Lab Kaiba is not on the roster", !banned);
                Check("Seto Kaiba DK list is on the roster", kaiba);
                Check("Every opponent deck file exists", filesOk);
                var cfg = OpponentCatalog.MakeMatch(OpponentCatalog.Get("dk_kaiba"), labTest: true, digital: true);
                Check("Opponent match 8000 LP no overlay",
                    cfg.StartingLp == 8000 && !cfg.DkOverlay
                    && cfg.AiDeckFile == "character_dk_kaiba.json");
            }

            {
                ArtifactCatalog.Invalidate();
                Check("Format DK badge in catalog", ArtifactCatalog.Get(ArtifactService.FormatId("dk")) != null);
                Check("Format DK shard in catalog", ArtifactCatalog.Get(ArtifactService.FormatPieceId("dk")) != null);
                var p = PlayerProgress.DefaultNew();
                var inv = PlayerInventory.Empty();
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.EnsureValid();
                ErazProgress.GrantTutorialBadge(p);
                ArtifactService.Grant(p, inv, ArtifactService.ErazId(ErazFormat.Original), 1);
                ArtifactService.Grant(p, inv, ArtifactService.FormatPieceId("dk"), 5);
                ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId("LOB"), 2500);
                var merged = FormatMergeService.TryMerge(p, inv, "dk", out var err);
                Check("Fuse DK format badge", merged && FormatProgress.HasBadge(p, "dk"), err);
                var storyCfg = MapZoneService.MakeStoryEra(
                    StoryCampaignService.Stage("s1_weevil"), "s1_weevil", digital: true);
                Check("Story match still no DK overlay after format badge",
                    !storyCfg.DkOverlay && storyCfg.StartingLp == 8000);
                Check("DK table laws are live", FormatProgress.TableLawsLive("dk"));
                Check("Raid table laws are not live", !FormatProgress.TableLawsLive("raid"));
                Check("Speed table laws are not live", !FormatProgress.TableLawsLive("speed"));
                Check("Can opt in DK after fuse", FormatProgress.CanOptInDuelistKingdom(p));
                var fresh = PlayerProgress.DefaultNew();
                Check("Cannot opt in DK without badge", !FormatProgress.CanOptInDuelistKingdom(fresh));
                var hub = ArDuelMatchConfig.DefaultQuick();
                hub.Launch = ArDuelLaunchKind.Hub;
                Check("Hub DK opt-in applies overlay",
                    ArDuelMatchConfig.TryApplyDuelistKingdomOptIn(hub, p)
                    && hub.DkOverlay && hub.StartingLp == 2000
                    && hub.FormatId == FormatProgress.DuelistKingdomId);
                Check("Story launch refuses DK opt-in",
                    !ArDuelMatchConfig.TryApplyDuelistKingdomOptIn(storyCfg, p)
                    && !storyCfg.DkOverlay && storyCfg.StartingLp == 8000);
                Check("Hub without badge refuses DK opt-in",
                    !ArDuelMatchConfig.TryApplyDuelistKingdomOptIn(
                        new ArDuelMatchConfig { Launch = ArDuelLaunchKind.Hub, StartingLp = 8000 },
                        fresh));
                var dk = ArDuelMatchConfig.DuelistKingdomPvAi();
                Check("DK PvAI factory 2000 LP overlay",
                    dk.DkOverlay && dk.StartingLp == 2000
                    && dk.Launch == ArDuelLaunchKind.Hub
                    && !ArDuelMatchConfig.IsStoryLaunch(dk.Launch));
            }

            sb.AppendLine($"Story campaign: {pass} pass, {fail} fail");
            return sb.ToString();
        }
    }
}
