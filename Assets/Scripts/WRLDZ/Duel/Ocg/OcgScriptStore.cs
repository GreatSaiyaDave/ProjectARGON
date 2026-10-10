using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>Allow-listed filename → file. Nested official~/ trees are indexed by basename (folder is ~ so Unity does not import 13k lua files).</summary>
    public static class OcgScriptStore
    {
        public const string EditorPrefsRootKey = "WRLDZ.OcgScriptsRoot";

        /// <summary>StreamingAssets-relative scripts root, read through <see cref="StreamingFiles"/> (APK-safe).</summary>
        public const string ScriptsDir = "OcgCore/scripts";

        /// <summary>Where one indexed script lives: StreamingAssets-relative, or an absolute disk path (Editor extra roots).</summary>
        readonly struct ScriptRef
        {
            public readonly bool Streaming;
            public readonly string Path;

            public ScriptRef(bool streaming, string path)
            {
                Streaming = streaming;
                Path = path;
            }

            public override string ToString() => Streaming ? StreamingFiles.FullPathForLog(Path) : Path;
        }

        static Dictionary<string, ScriptRef> _index;
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

        /// <summary>
        /// True when the basename c{passcode}.lua is present in the indexed OCG corpus. Index lookup only —
        /// no file read (this runs from activation-legality queries every drag frame, PERF-05).
        /// </summary>
        public static bool HasCardScript(int cardId)
        {
            if (cardId <= 0) return false;
            EnsureIndex();
            return _index != null && _index.ContainsKey("c" + cardId + ".lua");
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
            if (_index != null && _index.TryGetValue(name, out var script))
            {
                if (script.Streaming)
                {
                    if (StreamingFiles.TryReadAllBytes(script.Path, out bytes))
                        return true;
                }
                else if (File.Exists(script.Path))
                {
                    bytes = File.ReadAllBytes(script.Path);
                    PinEditorCwd();
                    return true;
                }
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
            _index = new Dictionary<string, ScriptRef>(StringComparer.OrdinalIgnoreCase);
            _indexError = null;
            IndexStreaming();
            foreach (var extra in ExtraRoots())
                IndexRoot(extra);
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

        /// <summary>Shipped corpus under StreamingAssets/OcgCore/scripts (preferred; duplicates are an error).</summary>
        static void IndexStreaming()
        {
            if (!StreamingFiles.DirectoryExists(ScriptsDir)) return;
            string[] files;
            try
            {
                files = StreamingFiles.GetFiles(ScriptsDir, "*.lua", recursive: true);
            }
            catch (Exception ex)
            {
                _indexError = "scan " + StreamingFiles.FullPathForLog(ScriptsDir) + ": " + ex.Message;
                return;
            }
            foreach (var rel in files)
            {
                if (IsExcludedPath("/" + rel))
                    continue;
                var slash = rel.LastIndexOf('/');
                var name = slash >= 0 ? rel.Substring(slash + 1) : rel;
                if (string.IsNullOrEmpty(name)) continue;
                if (_index.TryGetValue(name, out var existing))
                {
                    if (existing.Streaming && string.Equals(existing.Path, rel, StringComparison.Ordinal)) continue;
                    _indexError = "duplicate script basename " + name + " (" + existing + " vs " +
                                  StreamingFiles.FullPathForLog(rel) + ")";
                    continue;
                }
                _index[name] = new ScriptRef(true, rel);
            }
        }

        /// <summary>Editor-only extra roots on disk (OCG_SCRIPTS_ROOT / EditorPrefs); never override the shipped corpus.</summary>
        static void IndexRoot(string root)
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
                if (_index.ContainsKey(name))
                    continue;
                _index[name] = new ScriptRef(false, full);
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

        static void PinEditorCwd() => WRLDZ.Core.EditorWorkingDirectory.Pin();
    }
}
