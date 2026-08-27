using UnityEditor;
using UnityEngine;
using WRLDZ.Duel.Rules;

namespace WRLDZ.EditorTools
{
    public static class AiMasteryMenu
    {
        [MenuItem("WRLDZ/AI/Measure Pre-Link Set Coverage")]
        public static void MeasureCoverage()
        {
            var report = CardEraCurriculum.MeasureAllReport();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Pre-Link coverage", Truncate(report, 1400), "OK");
        }

        [MenuItem("WRLDZ/AI/Run Pre-Link Mastery Pass (24 duels × sets)")]
        public static void RunMasteryInteractive()
        {
            if (!EditorUtility.DisplayDialog(
                    "AI Mastery",
                    "Run self-play mastery on curriculum sets (lab decks + heuristics)?\n" +
                    "Exports review JSONL + mastery_report.txt.",
                    "Run", "Cancel"))
                return;
            var r = AiMasteryTrainer.Run(24);
            EditorUtility.DisplayDialog(r.Ok ? "Mastery OK" : "Mastery issues",
                Truncate(r.Summary, 1400), "OK");
        }

        [MenuItem("WRLDZ/AI/Run Quick Mastery (8 duels × sets)")]
        public static void RunQuick()
        {
            var r = AiMasteryTrainer.Run(8);
            EditorUtility.DisplayDialog(r.Ok ? "Quick mastery OK" : "Quick mastery issues",
                Truncate(r.Summary, 1400), "OK");
        }

        /// <summary>Batch: -executeMethod WRLDZ.EditorTools.AiMasteryMenu.RunMasteryBatch</summary>
        public static void RunMasteryBatch()
        {
            var n = 16;
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-masteryDuels" && int.TryParse(args[i + 1], out var v))
                    n = Mathf.Clamp(v, 1, 100);

            var r = AiMasteryTrainer.Run(n);
            EditorApplication.Exit(r.Ok ? 0 : 1);
        }

        static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "\n…");
    }
}
