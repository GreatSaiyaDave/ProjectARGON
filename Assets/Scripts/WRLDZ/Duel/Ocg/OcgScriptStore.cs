using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>Allow-listed filename → file. Nested official~/ trees are indexed by basename (folder is ~ so Unity does not import 13k lua files).</summary>
    public static class OcgScriptStore
    {
        public const string EditorPrefsRootKey = "WRLDZ.OcgScriptsRoot";

        public static string ScriptsDir =>
            Path.Combine(Application.streamingAssetsPath, "OcgCore", "scripts");

        static Dictionary<string, string> _index;
        static string _indexError;

        public static int IndexedCount
        {
            get
            {
                EnsureIndex();
                return _index != null ? _index.Count : 0;
            }
        }

        public static string IndexError
        {
            get
            {
                EnsureIndex();
                return _indexError;
            }
        }

        public static IEnumerable<string> IndexedNames
        {
            get
            {
                EnsureIndex();
                return _index != null ? _index.Keys : Array.Empty<string>();
            }
        }

        public static void Invalidate()
        {
            _index = null;
            _indexError = null;
        }

        /// <summary>True when the basename c{passcode}.lua is present in the indexed OCG corpus.</summary>
        public static bool HasCardScript(int cardId)
        {
            if (cardId <= 0) return false;
            return TryRead("c" + cardId + ".lua", out _, out _);
        }

        public static bool TryRead(string name, out byte[] bytes, out string error)
        {
            bytes = null;
            error = null;
            if (string.IsNullOrEmpty(name))
            {
                error = "empty script name";
                return false;
            }
            if (name.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                name.IndexOf('/') >= 0 ||
                name.IndexOf('\\') >= 0)
            {
                error = "rejected script name " + name;
                return false;
            }
            EnsureIndex();
            if (_index != null && _index.TryGetValue(name, out var full) && File.Exists(full))
            {
                bytes = File.ReadAllBytes(full);
                return true;
            }
            error = "missing " + name;
            return false;
        }

        public static int LoadGlobal(IntPtr duel, string name)
        {
            if (!TryRead(name, out var bytes, out var err))
            {
                Debug.LogError("[WRLDZ OCG] LoadGlobal " + name + ": " + err);
                return 0;
            }
            return OcgNativeApi.OCG_LoadScript(duel, bytes, (uint)bytes.Length, name);
        }

        static void EnsureIndex()
        {
            if (_index != null) return;
            _index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _indexError = null;
            IndexRoot(ScriptsDir, streamingPreferred: true);
            foreach (var extra in ExtraRoots())
                IndexRoot(extra, streamingPreferred: false);
        }

        static IEnumerable<string> ExtraRoots()
        {
#if UNITY_EDITOR
            var env = Environment.GetEnvironmentVariable("OCG_SCRIPTS_ROOT");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
                yield return env;
            var pref = UnityEditor.EditorPrefs.GetString(EditorPrefsRootKey, "");
            if (!string.IsNullOrEmpty(pref) && Directory.Exists(pref) &&
                !string.Equals(pref, env, StringComparison.Ordinal))
                yield return pref;
#endif
            yield break;
        }

        static void IndexRoot(string root, bool streamingPreferred)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            var fullRoot = Path.GetFullPath(root);
            string[] files;
            try
            {
                files = Directory.GetFiles(fullRoot, "*.lua", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                _indexError = "scan " + fullRoot + ": " + ex.Message;
                PinEditorCwd();
                return;
            }
            PinEditorCwd();
            foreach (var f in files)
            {
                var full = Path.GetFullPath(f);
                if (!full.StartsWith(fullRoot, StringComparison.Ordinal))
                    continue;
                if (IsExcludedPath(full))
                    continue;
                var name = Path.GetFileName(full);
                if (string.IsNullOrEmpty(name)) continue;
                if (_index.TryGetValue(name, out var existing))
                {
                    if (string.Equals(existing, full, StringComparison.Ordinal)) continue;
                    if (!streamingPreferred)
                        continue;
                    _indexError = "duplicate script basename " + name + " (" + existing + " vs " + full + ")";
                    continue;
                }
                _index[name] = full;
            }
        }

        static bool IsExcludedPath(string full)
        {
            var n = full.Replace('\\', '/');
            return ContainsDir(n, "/unofficial/") ||
                   ContainsDir(n, "/pre-release/") ||
                   ContainsDir(n, "/rush/") ||
                   ContainsDir(n, "/goat/") ||
                   ContainsDir(n, "/skill/") ||
                   ContainsDir(n, "/pre-errata/");
        }

        static bool ContainsDir(string path, string dir) =>
            path.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0;

        static void PinEditorCwd()
        {
#if UNITY_EDITOR
            try
            {
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                Directory.SetCurrentDirectory(root);
            }
            catch
            {
                // Editor guard also pins on compile.
            }
#endif
        }
    }
}
