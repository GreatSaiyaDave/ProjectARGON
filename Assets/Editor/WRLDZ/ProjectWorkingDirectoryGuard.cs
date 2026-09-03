using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.LowLevel;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Unity 6 fatals if process cwd leaves the project root (OpenXR SaveAssetIfDirty
    /// checks getcwd). Linux File.Exists / texture import / GetFiles can native-chdir
    /// into StreamingAssets (OpenDuelyst/icons, OcgCore/scripts, CardArt, …) without
    /// updating Directory.GetCurrentDirectory, so a managed match is not enough.
    /// Always chdir back before other Editor update callbacks.
    /// </summary>
    [InitializeOnLoad]
    static class ProjectWorkingDirectoryGuard
    {
        struct PinCwdLoop { }

        static readonly string ProjectRoot =
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static bool _warned;

        static ProjectWorkingDirectoryGuard()
        {
            Restore(force: true);
            InstallPlayerLoopPin();
            EditorApplication.update += () => Restore(force: true);
            EditorApplication.delayCall += () => Restore(force: true);
            EditorApplication.playModeStateChanged += _ => Restore(force: true);
            EditorApplication.wantsToQuit += () =>
            {
                Restore(force: true);
                return true;
            };
            EditorApplication.quitting += () => Restore(force: true);
            AssemblyReloadEvents.beforeAssemblyReload += () => Restore(force: true);
            AssemblyReloadEvents.afterAssemblyReload += () => Restore(force: true);
            CompilationPipeline.compilationStarted += _ => Restore(force: true);
            CompilationPipeline.compilationFinished += _ => Restore(force: true);
        }

        static void InstallPlayerLoopPin()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (loop.subSystemList != null)
            {
                foreach (var s in loop.subSystemList)
                    if (s.type == typeof(PinCwdLoop))
                        return;
            }

            var pin = new PlayerLoopSystem
            {
                type = typeof(PinCwdLoop),
                updateDelegate = () => Restore(force: true)
            };
            var list = new List<PlayerLoopSystem> { pin };
            if (loop.subSystemList != null)
                list.AddRange(loop.subSystemList);
            loop.subSystemList = list.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
        }

        internal static void Restore() => Restore(force: true);

        static void Restore(bool force)
        {
            try
            {
                var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
                var drifted = !string.Equals(cwd, ProjectRoot, System.StringComparison.Ordinal);
                // Always chdir on compile/reload: File.Exists on Linux can native-chdir
                // without updating Directory.GetCurrentDirectory(), so a managed match
                // is not proof Bee will find Library/Bee.
                if (!force && !drifted)
                    return;
                Directory.SetCurrentDirectory(ProjectRoot);
                if (!drifted || _warned)
                    return;
                _warned = true;
                Debug.LogWarning(
                    "[WRLDZ] Restored Editor working directory from '" + cwd +
                    "' to project root (Unity 6 Bee requires cwd == project root).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Failed to restore project working directory: " + ex.Message);
            }
        }

        /// <summary>Headless check: restore cwd, confirm project root, exit 0/1.</summary>
        public static void BatchVerifyWorkingDirectory()
        {
            Restore(force: true);
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

        void OnPreprocessTexture() => ProjectWorkingDirectoryGuard.Restore();

        void OnPostprocessTexture(Texture2D texture) => ProjectWorkingDirectoryGuard.Restore();

        static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            => ProjectWorkingDirectoryGuard.Restore();
    }
}
