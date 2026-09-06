using System.IO;
using UnityEditor;
using UnityEngine;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Phase 1 training menus: bulk compile starter/lab decks + coverage report + seed export.
    /// Batch: -executeMethod WRLDZ.EditorTools.EffectCoverageMenu.RunPhase1Batch
    /// </summary>
    public static class EffectCoverageMenu
    {
        [MenuItem("WRLDZ/Text Effects/Phase 1 — Compile Starter+Lab + Report + Seed")]
        public static void RunPhase1Menu()
        {
            var log = EffectCoverageService.RunPhase1(useAi: false);
            AssetDatabase.Refresh();
            Debug.Log("[WRLDZ TextFX] Phase 1 complete.\n" + log);
            EditorUtility.DisplayDialog(
                "Phase 1 — Effect coverage",
                Truncate(log, 1400),
                "OK");
        }

        [MenuItem("WRLDZ/Text Effects/Phase 1 — AI Compile Gaps (Starter+Lab)")]
        public static void RunPhase1WithAi()
        {
            if (!AiEffectCompileSettings.HasApiKey)
            {
                EditorUtility.DisplayDialog(
                    "No API key",
                    "Set XAI_API_KEY for offline AI compile of incomplete cards.\n" +
                    "Regex-only Phase 1 does not need a key.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "AI Phase 1",
                    "Regex-compile starter+lab decks, then SpaceXAI for incomplete cards.\n" +
                    "Exports seed + coverage report.\nContinue?",
                    "Run", "Cancel"))
                return;

            var log = EffectCoverageService.RunPhase1(useAi: true);
            AssetDatabase.Refresh();
            Debug.Log("[WRLDZ TextFX] Phase 1 AI complete.\n" + log);
            EditorUtility.DisplayDialog("Phase 1 AI done", Truncate(log, 1400), "OK");
        }

        [MenuItem("WRLDZ/Text Effects/Coverage Report Only (Starter+Lab)")]
        public static void ReportOnly()
        {
            var r = EffectCoverageService.MeasureStarterAndLab();
            Debug.Log(r.Detail);
            EditorUtility.DisplayDialog(
                "Coverage report",
                Truncate(r.Summary + "\n" + (r.ReportPath ?? "") + "\n" + (r.DiagnosticsPath ?? ""), 1400),
                "OK");
        }

        [MenuItem("WRLDZ/Text Effects/Coverage Report (entire cards_db)")]
        public static void ReportAllCardsDb()
        {
            var r = EffectCoverageService.MeasureAllCardsDb();
            var gaps = EffectCoverageService.SharedKindCompileGaps();
            var extra = gaps.Count == 0
                ? "shared-kind compile gaps: 0"
                : "shared-kind gaps:\n  " + string.Join("\n  ", gaps.GetRange(0, System.Math.Min(20, gaps.Count)));
            Debug.Log(r.Detail + "\n" + extra);
            EditorUtility.DisplayDialog(
                "cards_db coverage",
                Truncate(r.Summary + "\n" + extra + "\n" + (r.ReportPath ?? "") + "\n" + (r.DiagnosticsPath ?? ""), 1400),
                "OK");
        }

        [MenuItem("WRLDZ/Text Effects/Why-not-FullyCompiled Diagnostics (cards_db)")]
        public static void DiagnosticsAllCardsDb()
        {
            var r = EffectCoverageService.MeasureAllCardsDb();
            var path = r.DiagnosticsPath ?? "(JSON write failed)";
            Debug.Log("[WRLDZ TextFX] Why-not-FullyCompiled diagnostics → " + path);
            EditorUtility.DisplayDialog("Why-not-FullyCompiled diagnostics", path, "OK");
        }

        /// <summary>
        /// Headless batch entry. Writes report under persistentDataPath and seed to StreamingAssets.
        /// </summary>
        public static void RunPhase1Batch()
        {
            Debug.Log("[WRLDZ TextFX] Phase 1 batch starting…");
            var log = EffectCoverageService.RunPhase1(useAi: false);
            // Mirror report next to project for CI/agents
            try
            {
                var outDir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Tools", "WRLDZ");
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                var path = Path.Combine(outDir, "effect_coverage_phase1_latest.txt");
                File.WriteAllText(path, log);
                Debug.Log("[WRLDZ TextFX] Wrote " + path);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ TextFX] Project report copy failed: " + ex.Message);
            }

            Debug.Log("[WRLDZ TextFX] Phase 1 batch done.\n" + log);
#if UNITY_EDITOR
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
#endif
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "\n…";
        }
    }
}
