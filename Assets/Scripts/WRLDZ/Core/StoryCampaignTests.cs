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
                var day = new System.DateTime(2026, 1, 1, 12, 0, 0, System.DateTimeKind.Utc);
                for (var i = 0; i < 14; i++)
                {
                    if (QuestService.DailySetContains(day.ToString("yyyy-MM-dd"), QuestService.PlayStory))
                        break;
                    day = day.AddDays(1);
                }

                var p = PlayerProgress.DefaultNew();
                var q = QuestService.Ensure(p, day);
                Check("Three dailies", q != null && q.slots != null && q.slots.Length == 3);
                var match = new ArDuelMatchConfig { Launch = ArDuelLaunchKind.StoryEra };
                QuestService.CreditDuel(p, match, playerWon: true, day);
                q = QuestService.Ensure(p, day);
                var play = false;
                foreach (var s in q.slots)
                    if (s != null && s.id == QuestService.PlayStory)
                        play = s.progress >= 1;
                Check("Story duel credits daily", play);

                var inv = PlayerInventory.Empty();
                inv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                inv.EnsureValid();
                var claimed = false;
                foreach (var s in q.slots)
                {
                    if (s == null || s.progress < s.goal) continue;
                    claimed = QuestService.TryClaim(p, inv, s.id, day, out _);
                    break;
                }

                q = QuestService.Ensure(p, day);
                Check("First daily claim stamps the card", claimed && q.stamps == 1);
                Check("Daily claim pays Digizeni and Duel Coin (premium)",
                    ArtifactService.Qty(inv, ArtifactService.Digizeni) == QuestService.ClaimDigizeni &&
                    ArtifactService.Qty(inv, ArtifactService.DuelCoin) == QuestService.ClaimDuelCoin);
                var later = day.AddDays(2);
                q = QuestService.Ensure(p, later);
                Check("A missed day keeps the stamp", q.stamps == 1);
                var laterDay = q.dayUtc;
                q = QuestService.Ensure(p, day);
                Check("Rolled-back date keeps the later day (no fresh dailies)",
                    q.dayUtc == laterDay && q.stamps == 1);
                var backWeek = QuestService.Ensure(p, day.AddDays(-14));
                Check("Rolled-back week does not reset the weekly", backWeek.weekUtc == q.weekUtc);

                var wp = PlayerProgress.DefaultNew();
                var week = QuestService.MondayUtc(day);
                QuestService.CreditDuel(wp, new ArDuelMatchConfig { Launch = ArDuelLaunchKind.TearZone }, true, week);
                QuestService.CreditDuel(wp, new ArDuelMatchConfig { Launch = ArDuelLaunchKind.TearZone }, true, week);
                QuestService.CreditDuel(wp, new ArDuelMatchConfig { Launch = ArDuelLaunchKind.Practice }, true, week);
                q = QuestService.Ensure(wp, week);
                Check("Weekly counts wins and ignores practice", q.weeklyProgress == 2);
                QuestService.CreditDuel(wp, new ArDuelMatchConfig { Launch = ArDuelLaunchKind.TearZone }, true, week);
                var winv = PlayerInventory.Empty();
                winv.artifactDeckBox = new ArtifactDeckBoxState { owned = true };
                winv.EnsureValid();
                var weeklyClaimed = QuestService.TryClaim(wp, winv, QuestService.WeeklyWins, week, out _);
                Check("Weekly claim pays Digizeni and Duel Coin (premium)",
                    weeklyClaimed &&
                    ArtifactService.Qty(winv, ArtifactService.Digizeni) == QuestService.WeeklyDigizeni &&
                    ArtifactService.Qty(winv, ArtifactService.DuelCoin) == QuestService.WeeklyDuelCoin);
                Check("Weekly cannot be claimed twice",
                    !QuestService.TryClaim(wp, winv, QuestService.WeeklyWins, week, out _) &&
                    ArtifactService.Qty(winv, ArtifactService.DuelCoin) == QuestService.WeeklyDuelCoin);

                // pre-duel setup: a launch that dictates the format keeps its own LP; a free launch takes the choice
                var street = new ArDuelMatchConfig { Launch = ArDuelLaunchKind.NpcStreet, StartingLp = 4000 };
                DuelFormatCatalog.ApplyChoice(street, DuelFormatCatalog.StandardTcg);
                Check("Setup keeps a street duel at 4000 LP",
                    street.StartingLp == 4000 && street.RulesFormatId == DuelFormatCatalog.StandardTcg);
                var dk = new ArDuelMatchConfig { Launch = ArDuelLaunchKind.StoryEra, StartingLp = 2000, DkOverlay = true };
                DuelFormatCatalog.ApplyChoice(dk, DuelFormatCatalog.AdvancedTcg);
                Check("Setup keeps a Duelist Kingdom stage at 2000 LP with its overlay",
                    dk.StartingLp == 2000 && dk.DkOverlay && dk.RulesFormatId == DuelFormatCatalog.DuelistKingdom);
                var free = new ArDuelMatchConfig { Launch = ArDuelLaunchKind.Hub, StartingLp = 4000 };
                DuelFormatCatalog.ApplyChoice(free, DuelFormatCatalog.AdvancedTcg);
                Check("Setup applies the chosen format to a free launch",
                    free.StartingLp == TcgRules.StartingLifePoints && free.RulesFormatId == DuelFormatCatalog.AdvancedTcg);
                q = QuestService.Ensure(wp, week.AddDays(7));
                Check("Weekly resets the next Monday", q.weeklyProgress == 0);

                Check("Kuribandit story XP is +15%",
                    ProgressionService.ApplyRoleXp(1100, 1, KuribohTeam.Kuribandit,
                        new ArDuelMatchConfig { Launch = ArDuelLaunchKind.StoryEra }, true) == 1265);
                Check("Level 5 Tear win XP is +10%",
                    ProgressionService.ApplyRoleXp(900, 5, KuribohTeam.None,
                        new ArDuelMatchConfig { Launch = ArDuelLaunchKind.TearZone }, true) == 990);
                Check("Level 4 Tear win has no perk",
                    ProgressionService.ApplyRoleXp(900, 4, KuribohTeam.None,
                        new ArDuelMatchConfig { Launch = ArDuelLaunchKind.TearZone }, true) == 900);
                Check("Junkuriboh Tear SE is +10%",
                    ProgressionService.ApplyKuribohSetEnergy(4, KuribohTeam.Junkuriboh, ArDuelLaunchKind.TearZone) == 5);
                Check("Junkuriboh does not boost story SE",
                    ProgressionService.ApplyKuribohSetEnergy(4, KuribohTeam.Junkuriboh, ArDuelLaunchKind.StoryEra) == 4);

                var streak = PlayerProgress.DefaultNew();
                var duel = new ArDuelMatchConfig { Launch = ArDuelLaunchKind.TearZone };
                ShellLoops.NoteDuel(streak, duel, true);
                ShellLoops.NoteDuel(streak, duel, true);
                Check("Two wins do not bank a purse", streak.pveWinStreak == 2 && streak.streakPurses == 0);
                ShellLoops.NoteDuel(streak, duel, true);
                Check("Third win banks a purse and clears the streak",
                    streak.pveWinStreak == 0 && streak.streakPurses == 1);
                ShellLoops.NoteDuel(streak, duel, true);
                ShellLoops.NoteDuel(streak, duel, false);
                Check("A loss clears the streak and keeps the purse",
                    streak.pveWinStreak == 0 && streak.streakPurses == 1);
                ShellLoops.NoteDuel(streak, new ArDuelMatchConfig { Launch = ArDuelLaunchKind.Practice }, false);
                Check("Practice does not clear the purse", streak.streakPurses == 1);
                var story = new ArDuelMatchConfig { Launch = ArDuelLaunchKind.StoryEra };
                ShellLoops.NoteDuel(streak, story, true);
                Check("A won story duel adds Kuriboh bond", streak.kuribohBond == 1);
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
            }

            sb.AppendLine($"Story campaign: {pass} pass, {fail} fail");
            return sb.ToString();
        }
    }
}
