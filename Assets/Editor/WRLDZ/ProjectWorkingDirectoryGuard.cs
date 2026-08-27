using System.IO;
using UnityEditor;
using UnityEngine;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Unity 6 fatals if the Editor process cwd leaves the project root.
    /// Linux import of OpenDuelyst/units plus Google.VersionHandler.ImportAsset
    /// is the combination that triggered it.
    /// </summary>
    [InitializeOnLoad]
    static class ProjectWorkingDirectoryGuard
    {
        static readonly string ProjectRoot =
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static bool _warned;

        static ProjectWorkingDirectoryGuard()
        {
            Restore();
            EditorApplication.update += Restore;
            EditorApplication.delayCall += Restore;
            EditorApplication.playModeStateChanged += _ => Restore();
            EditorApplication.wantsToQuit += () =>
            {
                Restore();
                return true;
            };
            EditorApplication.quitting += Restore;
            AssemblyReloadEvents.beforeAssemblyReload += Restore;
            AssemblyReloadEvents.afterAssemblyReload += Restore;
        }

        internal static void Restore()
        {
            try
            {
                var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
                if (string.Equals(cwd, ProjectRoot, System.StringComparison.Ordinal))
                    return;
                Directory.SetCurrentDirectory(ProjectRoot);
                if (_warned) return;
                _warned = true;
                Debug.LogWarning(
                    "[WRLDZ] Restored Editor working directory from '" + cwd +
                    "' to project root (Unity 6 forbids cwd changes).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Failed to restore project working directory: " + ex.Message);
            }
        }

        /// <summary>Headless check: restore cwd, confirm project root, exit 0/1.</summary>
        public static void BatchVerifyWorkingDirectory()
        {
            Restore();
            var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
            if (!string.Equals(cwd, ProjectRoot, System.StringComparison.Ordinal))
            {
                Debug.LogError("[WRLDZ] cwd mismatch after restore: '" + cwd + "' vs '" + ProjectRoot + "'");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[WRLDZ] cwd ok: " + cwd);
            EditorApplication.Exit(0);
        }
    }

    sealed class ProjectWorkingDirectoryAssetHook : AssetPostprocessor
    {
        void OnPreprocessAsset() => ProjectWorkingDirectoryGuard.Restore();

        static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            => ProjectWorkingDirectoryGuard.Restore();
    }
}
