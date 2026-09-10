using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.LowLevel;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Unity 6 fatals if process cwd leaves the project root (OpenXR SaveAssetIfDirty
    /// checks getcwd). Linux File.Exists / texture import / GetFiles / git object walks
    /// can native-chdir into StreamingAssets or .git/objects/NN without updating
    /// Directory.GetCurrentDirectory. Managed SetCurrentDirectory is a no-op when Mono
    /// already thinks it is at the project root, so Restore also libc-chdir's.
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
                var managed = Path.GetFullPath(Directory.GetCurrentDirectory());
                var native = NativeGetcwd();
                var drifted = !IsProjectRoot(managed) || !IsProjectRoot(native);
                // Always chdir on compile/reload: File.Exists on Linux can native-chdir
                // without updating Directory.GetCurrentDirectory(), so a managed match
                // is not proof Bee will find Library/Bee.
                if (!force && !drifted)
                    return;

                // Mono no-ops SetCurrentDirectory when it already thinks cwd is
                // ProjectRoot, which leaves a native git/objects chdir in place.
                WRLDZ.Core.EditorWorkingDirectory.Pin();
                NativeChdir(ProjectRoot);
                Directory.SetCurrentDirectory(ProjectRoot);
                if (!drifted || _warned)
                    return;
                _warned = true;
                var from = !string.IsNullOrEmpty(native) ? native : managed;
                Debug.LogWarning(
                    "[WRLDZ] Restored Editor working directory from '" + from +
                    "' to project root (Unity 6 Bee requires cwd == project root).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Failed to restore project working directory: " + ex.Message);
            }
        }

        static bool IsProjectRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(path), ProjectRoot, System.StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        [DllImport("libc", EntryPoint = "chdir", SetLastError = true)]
        static extern int LibcChdir(string path);

        [DllImport("libc", EntryPoint = "getcwd", SetLastError = true)]
        static extern System.IntPtr LibcGetcwd(byte[] buf, System.IntPtr size);

        static void NativeChdir(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                LibcChdir(path);
            }
            catch
            {
                // libc missing (non-Linux) — managed SetCurrentDirectory is the fallback.
            }
        }

        static string NativeGetcwd()
        {
            try
            {
                var buf = new byte[4096];
                var p = LibcGetcwd(buf, new System.IntPtr(buf.Length));
                if (p == System.IntPtr.Zero) return null;
                var n = System.Array.IndexOf(buf, (byte)0);
                if (n < 0) n = buf.Length;
                return System.Text.Encoding.UTF8.GetString(buf, 0, n);
            }
            catch
            {
                return null;
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
