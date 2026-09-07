using System;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.EditorTools
{
    public static class TcgEngineTestsMenu
    {
        /// <summary>Shared report (menu, batch, and Pipeline CLI). Never quits the Editor.</summary>
        public static string RunTestsReport()
        {
            var report = TcgRegressionTests.RunAll() + "\n" + InteractionRegressionTests.RunAll() +
                         "\n" + CorpusTriggerStressTests.Run() +
                         "\n" + ChainRegressionTests.RunAll() +
                         "\n" + CoinDiceRegressionTests.RunAll() +
                         "\n" + RitualRegressionTests.RunAll() +
                         "\n" + WRLDZ.Core.InventoryRegressionTests.RunAll() +
                         "\n" + ArPlaymatLayout.RunSanityChecks() +
                         "\n" + WRLDZ.Presentation.ArPhysicalCardBuilder.RunFaceSwapSanity();
            Debug.Log("[WRLDZ TCG Tests]\n" + report);
            return report;
        }

        [MenuItem("WRLDZ/Rules/Run TCG Regression Tests")]
        public static void RunTests()
        {
            var report = RunTestsReport();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.Contains("FAIL  ") ? 1 : 0);
                return;
            }

            EditorUtility.DisplayDialog("WRLDZ TCG Tests", report, "OK");
        }

        /// <summary>Batchmode: Unity -executeMethod WRLDZ.EditorTools.TcgEngineTestsMenu.RunTestsBatch</summary>
        public static void RunTestsBatch() => RunTests();

        /// <summary>
        /// Live Editor: <c>unity command wrldz_tcg_tests</c>
        /// One-shot / reuse open Editor: <c>unity run ProjectARGON --command wrldz_tcg_tests</c>
        /// </summary>
        [CliCommand("wrldz_tcg_tests",
            "Run TCG + interaction + layout + disk face-swap regressions. Returns the report text.",
            MainThreadRequired = true)]
        public static string RunTcgTestsCli()
        {
            var report = RunTestsReport();
            if (report.IndexOf("FAIL  ", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("WRLDZ TCG tests failed.\n" + report);
            return report;
        }

        [MenuItem("WRLDZ/Lab/Run Playmat Layout Sanity")]
        public static void RunPlaymatSanity()
        {
            var report = ArPlaymatLayout.RunSanityChecks();
            Debug.Log("[WRLDZ LAYOUT]\n" + report);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.Contains("FAIL  ") ? 1 : 0);
                return;
            }

            EditorUtility.DisplayDialog("WRLDZ Playmat Layout", report, "OK");
        }

        [MenuItem("WRLDZ/Rules/Open Rulings Notes")]
        public static void OpenNotes()
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "Assets/StreamingAssets/Cards/RULINGS_NOTES.md");
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
            else
                Debug.LogWarning("RULINGS_NOTES.md not found under StreamingAssets/Cards/");
        }
    }
}
