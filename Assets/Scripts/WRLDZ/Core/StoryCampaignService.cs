using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    [Serializable]
    public class StoryCatalogFile
    {
        public int version;
        public int season;
        public string title;
        public string erazBandId;
        public StoryGateDef[] gates;
        public StoryStageDef[] stages;
    }

    [Serializable]
    public class StoryGateDef
    {
        public string id;
        public string title;
        public string stageIdsCsv;
    }

    [Serializable]
    public class StoryStageDef
    {
        public string id;
        public string gateId;
        public string title;
        public string opponentName;
        public string blurb;
        public string aiDeckFile;
        public string erazBandId;
        public bool dkOverlay;
        public int startingLp;
        public string setTagsCsv;
        public int rewardCardId;
        public int rewardSoulPieces;
        public string rewardErazPieceBand;
        public string unlocksStageId;
        public int seOnWin;
    }

    /// <summary>
    /// Season 1 Duelist Kingdom campaign. Catalog is JSON; progress is CSV on
    /// <see cref="PlayerProgress"/>. DuelEngine never reads this file.
    /// </summary>
    public static class StoryCampaignService
    {
        public const string RelativePath = "WRLDZ/Story/season1_dk.json";
        public const string FirstStageId = "s1_weevil";

        static StoryCatalogFile _cached;
        static Dictionary<string, StoryStageDef> _byId;
        static Dictionary<string, StoryGateDef> _gateById;

        public static void Invalidate()
        {
            _cached = null;
            _byId = null;
            _gateById = null;
        }

        public static StoryCatalogFile Load()
        {
            if (_cached != null) return _cached;
            var path = Path.Combine(Application.streamingAssetsPath, RelativePath);
            if (!File.Exists(path))
            {
                Debug.LogWarning("[WRLDZ Story] Missing " + path);
                _cached = Empty();
                Index();
                return _cached;
            }

            try
            {
                _cached = JsonUtility.FromJson<StoryCatalogFile>(File.ReadAllText(path))
                          ?? Empty();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Story] Parse failed: " + ex.Message);
                _cached = Empty();
            }

            _cached.gates ??= Array.Empty<StoryGateDef>();
            _cached.stages ??= Array.Empty<StoryStageDef>();
            Index();
            return _cached;
        }

        public static StoryStageDef Stage(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Load();
            return _byId != null && _byId.TryGetValue(id, out var s) ? s : null;
        }

        public static StoryGateDef Gate(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Load();
            return _gateById != null && _gateById.TryGetValue(id, out var g) ? g : null;
        }

        public static IReadOnlyList<StoryGateDef> Gates()
        {
            var file = Load();
            return file.gates ?? Array.Empty<StoryGateDef>();
        }

        public static IReadOnlyList<StoryStageDef> Stages()
        {
            var file = Load();
            return file.stages ?? Array.Empty<StoryStageDef>();
        }

        public static string[] StageIdsForGate(StoryGateDef gate)
        {
            if (gate == null || string.IsNullOrEmpty(gate.stageIdsCsv))
                return Array.Empty<string>();
            var parts = gate.stageIdsCsv.Split(',');
            var list = new List<string>();
            for (var i = 0; i < parts.Length; i++)
            {
                var s = parts[i].Trim();
                if (s.Length > 0) list.Add(s);
            }

            return list.ToArray();
        }

        public static void Ensure(PlayerProgress p)
        {
            if (p == null) return;
            p.storyClearedCsv ??= "";
            if (string.IsNullOrEmpty(p.storyCurrentId))
            {
                if (IsCleared(p, FirstStageId))
                {
                    var first = Stage(FirstStageId);
                    p.storyCurrentId = first != null ? first.unlocksStageId ?? "" : "";
                    AdvancePastCleared(p);
                }
                else
                    p.storyCurrentId = FirstStageId;
            }
        }

        public static bool IsCleared(PlayerProgress p, string stageId)
        {
            if (p == null || string.IsNullOrEmpty(stageId)) return false;
            return ContainsId(p.storyClearedCsv, stageId);
        }

        public static bool CanPlay(PlayerProgress p, string stageId)
        {
            if (p == null || string.IsNullOrEmpty(stageId)) return false;
            Ensure(p);
            if (Stage(stageId) == null) return false;
            if (IsCleared(p, stageId)) return true;
            return string.Equals(p.storyCurrentId, stageId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLocked(PlayerProgress p, string stageId)
        {
            if (Stage(stageId) == null) return true;
            return !CanPlay(p, stageId);
        }

        public static StoryStageDef Current(PlayerProgress p)
        {
            Ensure(p);
            return Stage(p?.storyCurrentId);
        }

        public static bool SeasonComplete(PlayerProgress p)
        {
            var stages = Stages();
            if (stages.Count == 0) return false;
            for (var i = 0; i < stages.Count; i++)
            {
                if (stages[i] == null || string.IsNullOrEmpty(stages[i].id)) continue;
                if (!IsCleared(p, stages[i].id)) return false;
            }

            return true;
        }

        /// <summary>
        /// First-clear persist only. Loot is applied by
        /// <see cref="GrantFirstClearRewards"/>. Replay returns false.
        /// </summary>
        public static bool TryComplete(PlayerProgress p, string stageId)
        {
            if (p == null || string.IsNullOrEmpty(stageId)) return false;
            var stage = Stage(stageId);
            if (stage == null) return false;
            Ensure(p);
            if (IsCleared(p, stageId)) return false;
            if (!string.Equals(p.storyCurrentId, stageId, StringComparison.OrdinalIgnoreCase))
                return false;

            p.storyClearedCsv = string.IsNullOrEmpty(p.storyClearedCsv)
                ? stageId
                : p.storyClearedCsv + "," + stageId;
            p.storyCurrentId = stage.unlocksStageId ?? "";
            return true;
        }

        public static string GrantFirstClearRewards(
            PlayerProgress p, PlayerInventory inv, StoryStageDef stage, out int seGained)
        {
            seGained = 0;
            if (p == null || inv == null || stage == null) return "";
            var bits = new List<string>();

            var se = Mathf.Max(0, stage.seOnWin);
            if (se > 0)
            {
                seGained = GrantTaggedSe(p, inv, stage.setTagsCsv, se);
                if (seGained > 0)
                    bits.Add("+" + seGained + " SE");
            }

            var souls = Mathf.Max(0, stage.rewardSoulPieces);
            if (souls > 0)
            {
                ArtifactService.Grant(p, inv, ArtifactService.SoulFragment, souls);
                bits.Add("+" + souls + " soul shard");
            }

            if (!string.IsNullOrEmpty(stage.rewardErazPieceBand))
            {
                ArtifactService.Grant(p, inv, ArtifactService.ErazPieceId(stage.rewardErazPieceBand), 1);
                bits.Add("ERAZ shard (" + stage.rewardErazPieceBand + ")");
            }

            if (stage.rewardCardId > 0)
            {
                SoulCardService.TryReceiveTcgCard(
                    p, inv, stage.rewardCardId, 1, atHome: true,
                    nowUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    out _, out _);
                bits.Add("card #" + stage.rewardCardId);
            }

            return bits.Count == 0 ? "" : string.Join(" · ", bits);
        }

        public static string[] SetTags(StoryStageDef stage)
        {
            if (stage == null || string.IsNullOrEmpty(stage.setTagsCsv))
                return Array.Empty<string>();
            var parts = stage.setTagsCsv.Split(',');
            var list = new List<string>();
            for (var i = 0; i < parts.Length; i++)
            {
                var s = parts[i].Trim().ToUpperInvariant();
                if (s.Length > 0) list.Add(s);
            }

            return list.ToArray();
        }

        static int GrantTaggedSe(PlayerProgress p, PlayerInventory inv, string tagsCsv, int amount)
        {
            var tags = tagsCsv ?? "";
            var parts = tags.Split(',');
            var granted = 0;
            var any = false;
            for (var i = 0; i < parts.Length; i++)
            {
                var set = parts[i].Trim().ToUpperInvariant();
                if (set.Length == 0) continue;
                any = true;
                if (!ArtifactService.CanEarnSetEnergy(p, set)) continue;
                ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId(set), amount);
                granted += amount;
            }

            if (!any && ArtifactService.CanEarnSetEnergy(p, "LOB"))
            {
                ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId("LOB"), amount);
                granted += amount;
            }

            return granted;
        }

        static void AdvancePastCleared(PlayerProgress p)
        {
            var guard = 0;
            while (guard++ < 32 && !string.IsNullOrEmpty(p.storyCurrentId) && IsCleared(p, p.storyCurrentId))
            {
                var st = Stage(p.storyCurrentId);
                if (st == null) break;
                p.storyCurrentId = st.unlocksStageId ?? "";
            }
        }

        static bool ContainsId(string csv, string id)
        {
            if (string.IsNullOrEmpty(csv) || string.IsNullOrEmpty(id)) return false;
            var parts = csv.Split(',');
            for (var i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i].Trim(), id, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static StoryCatalogFile Empty() => new()
        {
            gates = Array.Empty<StoryGateDef>(),
            stages = Array.Empty<StoryStageDef>()
        };

        static void Index()
        {
            _byId = new Dictionary<string, StoryStageDef>(StringComparer.OrdinalIgnoreCase);
            _gateById = new Dictionary<string, StoryGateDef>(StringComparer.OrdinalIgnoreCase);
            if (_cached?.stages != null)
            {
                foreach (var s in _cached.stages)
                {
                    if (s == null || string.IsNullOrEmpty(s.id)) continue;
                    _byId[s.id] = s;
                }
            }

            if (_cached?.gates != null)
            {
                foreach (var g in _cached.gates)
                {
                    if (g == null || string.IsNullOrEmpty(g.id)) continue;
                    _gateById[g.id] = g;
                }
            }
        }
    }
}
