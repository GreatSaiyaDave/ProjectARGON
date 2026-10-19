using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// What the Stone's slates show, read once per build (BUILD_PLAN §7: every datum from its source, nothing
    /// invented; a datum with no source is null / 0 and its line stays hidden). It writes nothing of its own. The
    /// accessors it calls normalise in memory exactly as the screens that own them do (EnsureProgress /
    /// EnsureInventory as the old hub did, StoryCampaignService.Current, QuestService.Ensure rolling today's slots,
    /// PlayerInventory.ActivePlayDeckIndex); none of them persists.
    /// </summary>
    public sealed class MenuV6Snapshot
    {
        public bool HasAccount;
        public string Name;
        public int Level = 1;
        public float Xp01;
        public string Title;                 // the avatar's title (AvatarAppearance.title)
        public KuribohTeam Team;
        public string TeamName;
        public AvatarAppearance Look;
        public PlayerProgress Progress;

        public string SeasonCaption = MenuV6Data.SeasonCaption;
        public int ChapterIndex;             // 1-based index of the current gate (0 = none)
        public int ChapterCount;
        public string ChapterTitle;          // the gate's title
        public int StagesCleared;
        public int StagesTotal;
        public int MeterPieces;              // round(8 × cleared / total) (§7, §11 F19)

        public string DeckName;              // null = no deck box
        public int DeckMain = -1;
        public int[] DeckCards;              // the first 3 main-deck cards (the box's hover cards, P1b)
        public int DeckMin = TcgRules.MainDeckMin;
        public int StartingLp;

        public int TearPins;
        public string QuestTitle;            // first unclaimed daily quest (null = none)
        public string QuestReward;
        public string BazaarLine = MenuV6Data.BazaarLine;
    }

    public static class MenuV6Data
    {
        /// <summary>The literal of the STORY sheet subtitle (SystemsSheets.BuildStory); the only season.</summary>
        public const string SeasonCaption = "Season 1 · Duelist Kingdom";

        /// <summary>The Bazaar's tablet sets (SystemsSheets.FillTablets codes) and the pack price / size constants.</summary>
        public static readonly string BazaarLine = "LOB · MRD · SRL · " +
                                                   StoneTabletService.PackCost.ToString("N0", CultureInfo.InvariantCulture) +
                                                   " SE of a set · one " + StoneTabletService.PackSize + "-card pack";

        public static MenuV6Snapshot Read()
        {
            var s = new MenuV6Snapshot();
            LocalAccountStore.Account acc = null;
            try
            {
                var session = AppSession.Ensure();   // a MonoBehaviour: explicit != null, not ?. (Unity null)
                acc = session != null ? session.Account : null;
            }
            catch (Exception ex) { Debug.LogWarning("[WRLDZ MenuV6] no session: " + ex.Message); }

            s.TearPins = OverworldUI.TearPinCount;
            try { s.StartingLp = DuelFormatCatalog.Get(DuelFormatCatalog.StandardTcg)?.StartingLp ?? 0; }
            catch { s.StartingLp = 0; }

            if (acc == null)
            {
                s.Name = "Duelist";   // the old hub's own fallback (DuelDiskMenuUI.BuildVisuals)
                s.TeamName = KuribohTeamInfo.DisplayName(KuribohTeam.None);
                s.Look = AvatarAppearance.Default();
                s.Title = s.Look?.title;
                return s;
            }

            s.HasAccount = true;
            acc.EnsureProgress();
            var p = acc.progress;
            s.Progress = p;
            s.Name = string.IsNullOrEmpty(acc.displayName) ? "Duelist" : acc.displayName;
            s.Look = acc.GetAvatarOrDefault();
            s.Title = s.Look?.title;
            if (p != null)
            {
                s.Level = p.level;
                try { s.Xp01 = Mathf.Clamp01(p.XpProgress01()); }
                catch { s.Xp01 = 0f; }
                s.Team = p.Team;
            }

            s.TeamName = KuribohTeamInfo.DisplayName(s.Team);
            ReadStory(s, p);
            ReadDeck(s, acc);
            ReadQuest(s, p);
            return s;
        }

        static void ReadStory(MenuV6Snapshot s, PlayerProgress p)
        {
            if (p == null) return;
            try
            {
                var gates = StoryCampaignService.Gates();
                var stages = StoryCampaignService.Stages();
                s.ChapterCount = gates.Count;
                s.StagesTotal = 0;
                s.StagesCleared = 0;
                for (var i = 0; i < stages.Count; i++)
                {
                    if (stages[i] == null || string.IsNullOrEmpty(stages[i].id)) continue;
                    s.StagesTotal++;
                    if (StoryCampaignService.IsCleared(p, stages[i].id)) s.StagesCleared++;
                }

                var cur = StoryCampaignService.Current(p);
                StoryGateDef gate = null;
                if (cur != null) gate = StoryCampaignService.Gate(cur.gateId);
                else if (StoryCampaignService.SeasonComplete(p) && gates.Count > 0) gate = gates[gates.Count - 1];
                if (gate != null)
                {
                    for (var i = 0; i < gates.Count; i++)
                        if (gates[i] != null && gates[i].id == gate.id)
                            s.ChapterIndex = i + 1;
                    s.ChapterTitle = gate.title;
                }

                s.MeterPieces = s.StagesTotal > 0
                    ? Mathf.Clamp((int)Math.Floor(8.0 * s.StagesCleared / s.StagesTotal + 0.5), 0, 8)
                    : 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] story read failed: " + ex.Message);
                s.ChapterIndex = 0;
            }
        }

        static void ReadDeck(MenuV6Snapshot s, LocalAccountStore.Account acc)
        {
            try
            {
                acc.EnsureInventory();
                var inv = acc.inventory;
                if (inv?.deckBoxes == null) return;
                var i = inv.ActivePlayDeckIndex;
                if (i < 0 || i >= inv.deckBoxes.Length || inv.deckBoxes[i] == null) return;
                var box = inv.deckBoxes[i];
                s.DeckName = box.name;
                s.DeckMain = box.main?.Length ?? 0;
                if (box.main != null && box.main.Length > 0)
                {
                    var n = Math.Min(3, box.main.Length);
                    s.DeckCards = new int[n];
                    for (var k = 0; k < n; k++) s.DeckCards[k] = box.main[k];
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] deck read failed: " + ex.Message);
            }
        }

        static void ReadQuest(MenuV6Snapshot s, PlayerProgress p)
        {
            if (p == null) return;
            try
            {
                var st = QuestService.Ensure(p);
                if (st?.slots == null) return;
                foreach (var q in st.slots)
                {
                    if (q == null || q.claimed || string.IsNullOrEmpty(q.title)) continue;
                    s.QuestTitle = q.title;
                    s.QuestReward = QuestService.RewardLine(q.id);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] quest read failed: " + ex.Message);
            }
        }

        /// <summary>"40/40" (main count / the TCG minimum), as the plaque shows it.</summary>
        public static string DeckCountLine(MenuV6Snapshot s) =>
            s == null || s.DeckMain < 0 ? null : s.DeckMain + "/" + s.DeckMin;

        public static IReadOnlyList<string> Lines(MenuV6Snapshot s) => new List<string>
        {
            "name=" + s.Name, "lv=" + s.Level, "xp01=" + s.Xp01.ToString("0.00", CultureInfo.InvariantCulture),
            "team=" + s.TeamName, "chapter=" + s.ChapterIndex + "/" + s.ChapterCount + " " + s.ChapterTitle,
            "stages=" + s.StagesCleared + "/" + s.StagesTotal + " pieces=" + s.MeterPieces,
            "deck=" + s.DeckName + " " + DeckCountLine(s), "lp=" + s.StartingLp, "tears=" + s.TearPins,
            "quest=" + s.QuestTitle + " " + s.QuestReward
        };
    }
}
