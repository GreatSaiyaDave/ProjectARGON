using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// Linux File.Exists / GetFiles can native-chdir into the file's folder while
    /// Mono still reports the project root. Unity 6 Bee then fatals. Pin both.
    /// </summary>
    public static class EditorWorkingDirectory
    {
        public static void Pin()
        {
#if UNITY_EDITOR
            try
            {
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                NativeChdir(root);
                Directory.SetCurrentDirectory(root);
            }
            catch
            {
                // Guard also pins on Editor update.
            }
#endif
        }

#if UNITY_EDITOR
        [DllImport("libc", EntryPoint = "chdir", SetLastError = true)]
        static extern int LibcChdir(string path);

        static void NativeChdir(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                LibcChdir(path);
            }
            catch
            {
                // non-Linux: managed SetCurrentDirectory is the fallback
            }
        }
#endif
    }
}
