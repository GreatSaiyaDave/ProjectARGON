using System;
using System.IO;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using WRLDZ.Duel.Rules;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Editor + batchmode entry for engine stress tests.
    /// Batch: Unity -batchmode -nographics -quit -projectPath ... \
    ///   -executeMethod WRLDZ.EditorTools.DuelStressMenu.RunStressBatch \
    ///   -logFile stress.log
    /// Optional: -stressDuels 40
    /// </summary>
    public static class DuelStressMenu
    {
        [MenuItem("WRLDZ/Rules/Run Engine Stress Tests (40 duels)")]
        public static void RunStressInteractive()
        {
            var report = DuelEngineStressTests.Run(40);
            EditorUtility.DisplayDialog(
                report.Ok ? "Stress PASS" : "Stress FAIL",
                Truncate(report.Summary, 1500),
                "OK");
        }

        [MenuItem("WRLDZ/Rules/Run Engine Stress Tests (120 duels)")]
        public static void RunStressHeavy()
        {
            if (!EditorUtility.DisplayDialog(
                    "Heavy stress",
                    "Run 120 AI-vs-AI lab duels + unit tests?\nThis may take a minute.",
                    "Run", "Cancel"))
                return;
            var report = DuelEngineStressTests.Run(120);
            EditorUtility.DisplayDialog(
                report.Ok ? "Stress PASS" : "Stress FAIL",
                Truncate(report.Summary, 1500),
                "OK");
        }

        [MenuItem("WRLDZ/Rules/Run TCG Regression + Quick Stress (10)")]
        public static void RunQuick()
        {
            var report = DuelEngineStressTests.Run(10);
            EditorUtility.DisplayDialog(
                report.Ok ? "Quick stress PASS" : "Quick stress FAIL",
                Truncate(report.Summary, 1500),
                "OK");
        }

        [CliCommand("wrldz_stress",
            "Run corpus trigger sweep + TCG/interaction units + N AI lab duels. Returns the report.",
            MainThreadRequired = true)]
        public static string RunStressCli()
        {
            var report = DuelEngineStressTests.Run(20);
            if (!report.Ok)
                throw new InvalidOperationException("WRLDZ stress failed.\n" + report.Summary);
            return report.Summary;
        }

        /// <summary>Batchmode entry point — always exits with code 0/1.</summary>
        public static void RunStressBatch()
        {
            var n = DuelEngineStressTests.DefaultDuelCount;
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-stressDuels" && int.TryParse(args[i + 1], out var v))
                    n = Mathf.Clamp(v, 1, 500);
            }

            Debug.Log($"[WRLDZ STRESS BATCH] starting duels={n}");
            var report = DuelEngineStressTests.Run(n);

            var outPath = Path.Combine(Directory.GetCurrentDirectory(), "wrldz_stress_report.txt");
            try
            {
                File.WriteAllText(outPath, report.Summary);
                Debug.Log("[WRLDZ STRESS BATCH] wrote " + outPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Could not write report file: " + ex.Message);
            }

            if (report.Ok)
            {
                Debug.Log("[WRLDZ STRESS BATCH] PASS");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[WRLDZ STRESS BATCH] FAIL");
                EditorApplication.Exit(1);
            }
        }

        static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "\n…");
    }
}
