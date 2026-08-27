using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Loads lab AI heuristics (JSONL from Tools/WRLDZ/external_logs/corpus or StreamingAssets)
    /// into a lightweight policy table used by <see cref="SimpleAi"/>.
    /// </summary>
    public static class AiHeuristicPolicy
    {
        static bool _loaded;
        static readonly List<string> HeuristicLines = new();

        // Tunables parsed from corpus (safe defaults match previous SimpleAi)
        public static int RaigekiMinFaceUpThreat { get; private set; } = 1;
        public static int RaigekiMinMonsterCount { get; private set; } = 2;
        public static int TrapHoleMinAtk { get; private set; } = 1000;
        public static int MirrorForceMinAtk { get; private set; } = 1500;
        public static int WabokuSkipBelowAtk { get; private set; } = 1200;
        public static int EqualTradeMinOwnMonsters { get; private set; } = 2;
        public static bool PreferSetFlipMonsters { get; private set; } = true;
        public static bool PreferPotOfGreed { get; private set; } = true;
        public static bool PreferSetTrapsBeforeEnd { get; private set; } = true;
        public static bool UseSwordsWhenBehind { get; private set; } = true;
        public static bool PreferFlipBugs { get; private set; } = true;

        public static IReadOnlyList<string> LoadedLines => HeuristicLines;
        public static bool IsLoaded => _loaded;

        /// <summary>Load once per process (safe to call every AI turn).</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            HeuristicLines.Clear();

            foreach (var path in CandidatePaths())
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var n = 0;
                    foreach (var line in File.ReadLines(path))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line[0] != '{') continue;
                        ParseAndApply(line);
                        n++;
                    }

                    Debug.Log($"[WRLDZ AI Policy] Loaded {n} heuristics from {path}");
                    if (n > 0) return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ AI Policy] Load failed " + path + ": " + ex.Message);
                }
            }

            Debug.Log("[WRLDZ AI Policy] No external heuristics file — using built-in defaults.");
        }

        static IEnumerable<string> CandidatePaths()
        {
            // 1) Shipped with build
            yield return Path.Combine(Application.streamingAssetsPath, "WRLDZ", "ai_heuristics_v1.jsonl");
            // 2) Dev tree relative to project (Editor / standalone cwd)
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "Tools", "WRLDZ", "external_logs", "corpus", "lab_ai_heuristics_v1.jsonl"));
            // 3) Persistent override
            yield return Path.Combine(Application.persistentDataPath, "WRLDZ", "ai_heuristics_v1.jsonl");
        }

        static void ParseAndApply(string jsonLine)
        {
            HeuristicDto dto = null;
            try
            {
                dto = JsonUtility.FromJson<HeuristicDto>(jsonLine);
            }
            catch
            {
                return;
            }

            if (dto == null || string.IsNullOrEmpty(dto.message)) return;
            HeuristicLines.Add(dto.message);
            ApplyMessage(dto.message);
        }

        static void ApplyMessage(string msg)
        {
            var m = msg ?? "";
            // Soft parse of known heuristic phrasing
            if (m.IndexOf("Pot of Greed", StringComparison.OrdinalIgnoreCase) >= 0)
                PreferPotOfGreed = true;
            if (m.IndexOf("Raigeki", StringComparison.OrdinalIgnoreCase) >= 0 &&
                m.IndexOf("2+", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                RaigekiMinFaceUpThreat = 1;
                RaigekiMinMonsterCount = 2;
            }

            if (m.IndexOf("Trap Hole", StringComparison.OrdinalIgnoreCase) >= 0 &&
                m.IndexOf("1000", StringComparison.OrdinalIgnoreCase) >= 0)
                TrapHoleMinAtk = 1000;

            if (m.IndexOf("Mirror Force", StringComparison.OrdinalIgnoreCase) >= 0 &&
                m.IndexOf("1500", StringComparison.OrdinalIgnoreCase) >= 0)
                MirrorForceMinAtk = 1500;

            if (m.IndexOf("Waboku", StringComparison.OrdinalIgnoreCase) >= 0 &&
                m.IndexOf("1200", StringComparison.OrdinalIgnoreCase) >= 0)
                WabokuSkipBelowAtk = 1200;

            if (m.IndexOf("equal ATK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Equal ATK", StringComparison.OrdinalIgnoreCase) >= 0)
                EqualTradeMinOwnMonsters = 2;

            if (m.IndexOf("Set Flip", StringComparison.OrdinalIgnoreCase) >= 0)
                PreferSetFlipMonsters = true;

            if (m.IndexOf("Man-Eater", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Flip monsters", StringComparison.OrdinalIgnoreCase) >= 0)
                PreferFlipBugs = true;

            if (m.IndexOf("Set Mirror Force", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.IndexOf("Set traps", StringComparison.OrdinalIgnoreCase) >= 0)
                PreferSetTrapsBeforeEnd = true;

            if (m.IndexOf("Swords", StringComparison.OrdinalIgnoreCase) >= 0 &&
                m.IndexOf("behind", StringComparison.OrdinalIgnoreCase) >= 0)
                UseSwordsWhenBehind = true;
        }

        /// <summary>Dev-only once-per-process summary (not per-turn duel log spam).</summary>
        public static void LogPolicySummary(DuelEngine engine)
        {
            if (!_loggedSummaryOnce)
            {
                _loggedSummaryOnce = true;
                Debug.Log(
                    $"[WRLDZ AI Policy] ready lines={HeuristicLines.Count} " +
                    $"PoG={PreferPotOfGreed} setFlip={PreferSetFlipMonsters} " +
                    $"MF≥{MirrorForceMinAtk} TH≥{TrapHoleMinAtk} WabokuSkip<{WabokuSkipBelowAtk}");
            }
        }

        static bool _loggedSummaryOnce;

        [Serializable]
        class HeuristicDto
        {
            public string kind;
            public string message;
            public string extra;
        }
    }
}
