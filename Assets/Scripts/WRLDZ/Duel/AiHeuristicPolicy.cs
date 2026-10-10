using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Duel;

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

        // ── Difficulty-scaled tunables ──────────────────────────────────────────────
        public static bool UseAdvantageSpells { get; private set; }
        public static bool UseGraveyardRecursion { get; private set; }
        public static bool UseSearchCards { get; private set; }
        public static bool CalculateLethal { get; private set; }
        public static bool RespectBackrow { get; private set; }
        public static bool BaitTraps { get; private set; }
        public static bool UseChainResponse { get; private set; }
        public static bool PreserveResources { get; private set; }
        public static bool PreferSynchroFusion { get; private set; }
        public static bool DetectArchetype { get; private set; }
        public static bool UseExtraDeck { get; private set; }
        public static bool OptimizeHand { get; private set; }
        public static bool ValueCardAdvantage { get; private set; }
        public static bool AvoidSacrifice { get; private set; }
        public static bool AggressivePlay { get; private set; }
        public static bool DefensivePlay { get; private set; }

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

        /// <summary>
        /// DEF the AI assumes for a face-down monster when choosing an attack target. GDD 3.2: the AI
        /// does not look at face-down cards, so it plans with this public estimate, not the real DEF.
        /// </summary>
        public static int FaceDownDefEstimate { get; set; } = 1500;

        /// <summary>Apply difficulty-scaled tunables. Call once per duel start.</summary>
        public static void ApplyDifficulty(AiDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AiDifficulty.Easy:
                    UseAdvantageSpells = false;
                    UseGraveyardRecursion = false;
                    UseSearchCards = false;
                    CalculateLethal = false;
                    RespectBackrow = false;
                    BaitTraps = false;
                    UseChainResponse = false;
                    PreserveResources = false;
                    PreferSynchroFusion = false;
                    DetectArchetype = false;
                    UseExtraDeck = false;
                    OptimizeHand = false;
                    ValueCardAdvantage = false;
                    AvoidSacrifice = false;
                    AggressivePlay = false;
                    DefensivePlay = false;
                    FaceDownDefEstimate = 1000;
                    break;
                case AiDifficulty.Normal:
                    UseAdvantageSpells = true;
                    UseGraveyardRecursion = true;
                    UseSearchCards = true;
                    CalculateLethal = true;
                    RespectBackrow = false;
                    BaitTraps = false;
                    UseChainResponse = false;
                    PreserveResources = false;
                    PreferSynchroFusion = false;
                    DetectArchetype = false;
                    UseExtraDeck = false;
                    OptimizeHand = false;
                    ValueCardAdvantage = true;
                    AvoidSacrifice = true;
                    AggressivePlay = false;
                    DefensivePlay = true;
                    FaceDownDefEstimate = 1500;
                    break;
                case AiDifficulty.Hard:
                    UseAdvantageSpells = true;
                    UseGraveyardRecursion = true;
                    UseSearchCards = true;
                    CalculateLethal = true;
                    RespectBackrow = true;
                    BaitTraps = true;
                    UseChainResponse = true;
                    PreserveResources = true;
                    PreferSynchroFusion = true;
                    DetectArchetype = false;
                    UseExtraDeck = true;
                    OptimizeHand = true;
                    ValueCardAdvantage = true;
                    AvoidSacrifice = true;
                    AggressivePlay = true;
                    DefensivePlay = true;
                    FaceDownDefEstimate = 1800;
                    break;
                case AiDifficulty.Expert:
                    UseAdvantageSpells = true;
                    UseGraveyardRecursion = true;
                    UseSearchCards = true;
                    CalculateLethal = true;
                    RespectBackrow = true;
                    BaitTraps = true;
                    UseChainResponse = true;
                    PreserveResources = true;
                    PreferSynchroFusion = true;
                    DetectArchetype = true;
                    UseExtraDeck = true;
                    OptimizeHand = true;
                    ValueCardAdvantage = true;
                    AvoidSacrifice = true;
                    AggressivePlay = true;
                    DefensivePlay = true;
                    FaceDownDefEstimate = 2000;
                    break;
            }
        }

        public static IReadOnlyList<string> LoadedLines => HeuristicLines;
        public static bool IsLoaded => _loaded;

        /// <summary>
        /// Keep-value of a card in hand for automatic discards (hand size limit, discard costs):
        /// lower is discarded first. Monsters by their best printed stat, Spells above Traps above
        /// weak monsters, win-condition pieces (Exodia) never first.
        /// </summary>
        public static int DiscardPriority(CardInstance card)
        {
            var def = card?.Def;
            if (def == null) return 0;
            var name = def.name ?? "";
            if (name.IndexOf("Exodia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Forbidden One", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1000;
            if (def.IsMonster)
            {
                var best = Math.Max(def.atk, def.def);
                return 10 + Math.Max(0, best) / 100; // 10 … ~60
            }

            if (def.IsSpell) return 32;
            if (def.IsTrap) return 30;
            return 10;
        }

        /// <summary>
        /// Lowest <see cref="DiscardPriority"/> card in <paramref name="hand"/>, skipping
        /// <paramref name="except"/> (the card paying the cost). Ties go to the card nearest the end of
        /// the hand. Null when nothing can be discarded.
        /// </summary>
        public static CardInstance PickLowestValue(IList<CardInstance> hand, CardInstance except)
        {
            if (hand == null) return null;
            CardInstance best = null;
            var bestValue = int.MaxValue;
            for (var i = hand.Count - 1; i >= 0; i--)
            {
                var c = hand[i];
                if (c == null || c == except) continue;
                var v = DiscardPriority(c);
                if (v >= bestValue) continue;
                best = c;
                bestValue = v;
            }

            return best;
        }

        /// <summary>Load once per process (safe to call every AI turn).</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            HeuristicLines.Clear();

            foreach (var (streaming, candidate) in CandidatePaths())
            {
                if (streaming ? !StreamingFiles.Exists(candidate) : !File.Exists(candidate)) continue;
                var path = streaming ? StreamingFiles.FullPathForLog(candidate) : candidate;
                try
                {
                    var n = 0;
                    var lines = streaming ? StreamingFiles.ReadLines(candidate) : File.ReadLines(candidate);
                    foreach (var line in lines)
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

        /// <summary>(streaming, path): streaming paths are StreamingAssets-relative (APK-safe), the rest are disk paths.</summary>
        static IEnumerable<(bool streaming, string path)> CandidatePaths()
        {
            // 1) Shipped with build
            yield return (true, "WRLDZ/ai_heuristics_v1.jsonl");
            // 2) Dev tree relative to project (Editor / standalone cwd)
            yield return (false, Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "Tools", "WRLDZ", "external_logs", "corpus", "lab_ai_heuristics_v1.jsonl")));
            // 3) Persistent override
            yield return (false, Path.Combine(Application.persistentDataPath, "WRLDZ", "ai_heuristics_v1.jsonl"));
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
