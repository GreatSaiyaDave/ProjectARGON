using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Menu items to build a debug APK for Galaxy S23 Ultra (lab device).
    /// No headset / OpenXR required.
    /// </summary>
    public static class S23AndroidBuild
    {
        const string OutputDir = "Builds/Android";
        const string ApkName = "WRLDZ_S23_Debug.apk";

        [MenuItem("WRLDZ/Lab/Build APK for S23 Ultra")]
        public static void BuildApk()
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[WRLDZ] Failed to switch to Android build target. Install Android Build Support.");
                return;
            }

            // Portrait default + IL2CPP ARM64 (S23). Landscape allowed for salvage OST session.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            var androidTarget = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(androidTarget, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApplicationIdentifier(androidTarget, "com.wrldz.duelmonsters");
            PlayerSettings.companyName = "WRLDZ";
            PlayerSettings.productName = "Duel Monsters WRLDZ";

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[WRLDZ] No scenes in Build Settings.");
                return;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", OutputDir));
            var apkPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir, ApkName));

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            Debug.Log($"[WRLDZ Lab] Building S23 APK → {apkPath}");
            var report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result == BuildResult.Succeeded)
                Debug.Log($"[WRLDZ Lab] SUCCESS · {apkPath} · size={report.summary.totalSize / (1024 * 1024)}MB · time={report.summary.totalTime}");
            else
                Debug.LogError($"[WRLDZ Lab] BUILD FAILED · {report.summary.result} · errors={report.summary.totalErrors}");
        }

        [MenuItem("WRLDZ/Lab/Open S23 Lab Notes")]
        public static void OpenNotes()
        {
            var path = Path.Combine(Application.dataPath, "Scripts/WRLDZ/S23_LAB.md");
            if (File.Exists(path))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    "Assets/Scripts/WRLDZ/S23_LAB.md");
                if (obj != null)
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }
                Debug.Log("[WRLDZ Lab] Notes: " + path);
            }
            else
                Debug.LogWarning("[WRLDZ] S23_LAB.md missing at " + path);
        }

        [MenuItem("WRLDZ/Lab/Log Presentation Mode")]
        public static void LogMode()
        {
            Debug.Log(
                $"[WRLDZ Lab] mode={WRLDZ.Presentation.ArPresentationTarget.Current} · " +
                WRLDZ.Presentation.ArPresentationTarget.StatusLabel());
        }
    }
}
