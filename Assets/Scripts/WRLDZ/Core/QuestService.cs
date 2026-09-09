using System;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    [Serializable]
    public class QuestStateFile
    {
        public string dayUtc;
        public QuestSlot[] slots;
    }

    [Serializable]
    public class QuestSlot
    {
        public string id;
        public string title;
        public int progress;
        public int goal;
        public bool claimed;
    }

    /// <summary>
    /// Referobot dailies. Three easy tasks from events the game already emits.
    /// State is JSON on <see cref="PlayerProgress.questStateJson"/>.
    /// </summary>
    public static class QuestService
    {
        public const string WinStreetOrTear = "win_street_or_tear";
        public const string HarvestTear = "harvest_tear";
        public const string PlayStory = "play_story";

        public const int ClaimDigizeni = 40;
        public const int ClaimSe = 15;

        public static QuestStateFile Ensure(PlayerProgress p)
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (p == null) return Fresh(today);
            var state = Parse(p.questStateJson);
            if (state == null || state.dayUtc != today || state.slots == null || state.slots.Length != 3)
            {
                state = Fresh(today);
                Save(p, state);
            }

            return state;
        }

        public static void CreditDuel(PlayerProgress p, ArDuelMatchConfig match, bool playerWon)
        {
            if (p == null || match == null) return;
            var state = Ensure(p);
            if (playerWon && (match.Launch == ArDuelLaunchKind.TearZone
                || match.Launch == ArDuelLaunchKind.NpcStreet
                || match.Launch == ArDuelLaunchKind.TearBoss))
                Bump(state, WinStreetOrTear);
            if (match.Launch == ArDuelLaunchKind.StoryEra)
                Bump(state, PlayStory);
            Save(p, state);
        }

        public static void CreditHarvest(PlayerProgress p)
        {
            if (p == null) return;
            var state = Ensure(p);
            Bump(state, HarvestTear);
            Save(p, state);
        }

        public static bool TryClaim(PlayerProgress p, PlayerInventory inv, string questId, out string error)
        {
            error = null;
            if (p == null || inv == null)
            {
                error = "No account.";
                return false;
            }

            var state = Ensure(p);
            var slot = Find(state, questId);
            if (slot == null)
            {
                error = "Unknown task.";
                return false;
            }

            if (slot.claimed)
            {
                error = "Already claimed.";
                return false;
            }

            if (slot.progress < slot.goal)
            {
                error = "Not finished.";
                return false;
            }

            slot.claimed = true;
            ArtifactService.Grant(p, inv, ArtifactService.Digizeni, ClaimDigizeni);
            if (ArtifactService.CanEarnSetEnergy(p, "LOB"))
                ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId("LOB"), ClaimSe);
            Save(p, state);
            return true;
        }

        static void Bump(QuestStateFile state, string id)
        {
            var slot = Find(state, id);
            if (slot == null || slot.claimed) return;
            if (slot.progress < slot.goal)
                slot.progress++;
        }

        static QuestSlot Find(QuestStateFile state, string id)
        {
            if (state?.slots == null || string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < state.slots.Length; i++)
            {
                var s = state.slots[i];
                if (s != null && string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            return null;
        }

        static QuestStateFile Fresh(string dayUtc) => new()
        {
            dayUtc = dayUtc,
            slots = new[]
            {
                new QuestSlot
                {
                    id = WinStreetOrTear,
                    title = "Win a Tear or street duel",
                    goal = 1
                },
                new QuestSlot
                {
                    id = HarvestTear,
                    title = "Harvest a Tear",
                    goal = 1
                },
                new QuestSlot
                {
                    id = PlayStory,
                    title = "Play a Story stage",
                    goal = 1
                }
            }
        };

        static QuestStateFile Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<QuestStateFile>(json);
            }
            catch
            {
                return null;
            }
        }

        static void Save(PlayerProgress p, QuestStateFile state)
        {
            if (p == null) return;
            p.questStateJson = state == null ? "" : JsonUtility.ToJson(state);
        }
    }
}
