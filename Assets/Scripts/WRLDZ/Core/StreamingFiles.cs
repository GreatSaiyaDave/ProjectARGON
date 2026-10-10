using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// The one read path into StreamingAssets (see Core/STREAMING_FILES.md). Every API takes a path
    /// RELATIVE to the StreamingAssets root ("Cards/cards_db.json", forward or back slashes).
    ///
    /// Desktop / Editor / iOS: plain System.IO on <see cref="Application.streamingAssetsPath"/>, the
    /// same calls the loaders made before, plus the Linux Editor working-directory pin they each
    /// did by hand (<see cref="EditorWorkingDirectory"/>).
    ///
    /// Android (the shipping target: Android XR glasses APK; S23 is the dev testbed): streamingAssetsPath
    /// is <c>jar:file://…/base.apk!/assets</c>, which System.IO cannot open. Here the APK itself is
    /// opened once as a zip, every entry under <c>assets/</c> is indexed (case-sensitive, like the
    /// device file system), and reads are synchronous zip-entry reads. StreamingAssets are stored
    /// uncompressed in the APK; deflated entries are handled too. When an entry is missing from the
    /// base APK, the split APKs installed beside it (app-bundle installs: asset packs such as
    /// UnityStreamingAssetsPack) and <see cref="Application.dataPath"/> are indexed once and retried.
    ///
    /// Thread-safe: all archive access is under one lock, so worker threads may read.
    /// Writes never go through here: persistentDataPath / temporaryCachePath stay on System.IO.
    /// </summary>
    public static class StreamingFiles
    {
        const string Tag = "[WRLDZ StreamingFiles]";
        const string ApkAssetsPrefix = "assets/";
        /// <summary>Unity's own player data lives in assets/bin/Data — never StreamingAssets content.</summary>
        const string ApkPlayerDataPrefix = "bin/Data/";
        /// <summary>Small-text LRU (archive mode only; the APK is immutable so it can never go stale).</summary>
        const int TextCacheMaxBytes = 64 * 1024;
        const int TextCacheCapacity = 48;

        sealed class ApkSource
        {
            public string Path;
            public FileStream Stream;
            public ZipArchive Zip;
        }

        sealed class Entry
        {
            public ApkSource Source;
            public ZipArchiveEntry Zip;
        }

        static readonly object Gate = new object();
        static bool _init;
        static string _root = "";
        static bool _archive;
        static string _primaryApk;
        static string _dataPath;
        static string _cacheDir;
        static string _buildStamp;
        static int _mainThreadId = -1;

        static readonly List<ApkSource> Sources = new List<ApkSource>();
        static readonly Dictionary<string, Entry> Files = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static readonly HashSet<string> Dirs = new HashSet<string>(StringComparer.Ordinal);
        static bool _secondaryIndexed;
        static bool _loggedMiss;
        static bool _loggedOpenFailure;

        static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> TextCache =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        static readonly LinkedList<KeyValuePair<string, string>> TextLru = new LinkedList<KeyValuePair<string, string>>();

        /// <summary>Capture the Application paths on the main thread before any worker can ask.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot()
        {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            EnsureInit();
        }

#if UNITY_EDITOR
        /// <summary>Edit-mode callers (menus, smoke tests) never hit the runtime hook; record the main thread here.</summary>
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorBoot() => _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif

        /// <summary>Unity's StreamingAssets root (a jar: URL on Android). For logs and URLs only.</summary>
        public static string Root
        {
            get
            {
                EnsureInit();
                return _root;
            }
        }

        /// <summary>True when reads are served from the APK zip (Android player).</summary>
        public static bool IsArchive
        {
            get
            {
                EnsureInit();
                return _archive;
            }
        }

        // ─────────────────────────────── public API ───────────────────────────────

        public static bool Exists(string rel)
        {
            EnsureInit();
            var key = Normalize(rel);
            if (key.Length == 0) return false;
            if (_archive) return TryEntry(key, out _);
            try { return File.Exists(Full(key)); }
            finally { Pin(); }
        }

        public static bool DirectoryExists(string rel)
        {
            EnsureInit();
            var key = Normalize(rel);
            if (_archive)
            {
                if (key.Length == 0) return true;
                lock (Gate)
                {
                    if (Dirs.Contains(key)) return true;
                    IndexSecondaryLocked();
                    return Dirs.Contains(key);
                }
            }
            try { return Directory.Exists(Full(key)); }
            finally { Pin(); }
        }

        /// <summary>
        /// Change stamp for cache invalidation. Desktop: file length + last-write ticks (re-pins the Editor cwd
        /// like every other desktop call). Android: APK entries are immutable, so length is the entry size and
        /// ticks is 0. False when the file is missing.
        /// </summary>
        public static bool TryGetStamp(string rel, out long length, out long ticks)
        {
            EnsureInit();
            length = 0;
            ticks = 0;
            var key = Normalize(rel);
            if (key.Length == 0) return false;
            if (_archive)
            {
                if (!TryEntry(key, out var entry)) return false;
                length = entry.Zip != null ? entry.Zip.Length : 0;
                return true;
            }
            try
            {
                var fi = new FileInfo(Full(key));
                if (!fi.Exists) return false;
                length = fi.Length;
                ticks = fi.LastWriteTimeUtc.Ticks;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally { Pin(); }
        }

        /// <summary>Whole file. Throws <see cref="FileNotFoundException"/> when missing, like File.ReadAllBytes.</summary>
        public static byte[] ReadAllBytes(string rel)
        {
            if (TryReadAllBytes(rel, out var bytes)) return bytes;
            throw new FileNotFoundException("StreamingAssets file not found: " + FullPathForLog(rel), Normalize(rel));
        }

        /// <summary>Whole file as UTF-8 (BOM honoured). Throws <see cref="FileNotFoundException"/> when missing.</summary>
        public static string ReadAllText(string rel)
        {
            if (TryReadAllText(rel, out var text)) return text;
            throw new FileNotFoundException("StreamingAssets file not found: " + FullPathForLog(rel), Normalize(rel));
        }

        public static bool TryReadAllBytes(string rel, out byte[] bytes)
        {
            EnsureInit();
            bytes = null;
            var key = Normalize(rel);
            if (key.Length == 0) return false;
            if (_archive)
            {
                lock (Gate)
                {
                    if (!TryEntryLocked(key, out var e)) return false;
                    bytes = ReadEntryLocked(e);
                    return bytes != null;
                }
            }

            try
            {
                var full = Full(key);
                if (!File.Exists(full)) return false;
                bytes = File.ReadAllBytes(full);
                return true;
            }
            finally { Pin(); }
        }

        public static bool TryReadAllText(string rel, out string text)
        {
            EnsureInit();
            text = null;
            var key = Normalize(rel);
            if (key.Length == 0) return false;
            if (_archive)
            {
                lock (Gate)
                {
                    if (TextCache.TryGetValue(key, out var node))
                    {
                        TextLru.Remove(node);
                        TextLru.AddFirst(node);
                        text = node.Value.Value;
                        return true;
                    }

                    if (!TryEntryLocked(key, out var e)) return false;
                    var bytes = ReadEntryLocked(e);
                    if (bytes == null) return false;
                    text = Decode(bytes);
                    if (bytes.Length <= TextCacheMaxBytes) RememberTextLocked(key, text);
                    return true;
                }
            }

            try
            {
                var full = Full(key);
                if (!File.Exists(full)) return false;
                text = File.ReadAllText(full);
                return true;
            }
            finally { Pin(); }
        }

        /// <summary>Lines of a text file (File.ReadLines equivalent; empty when missing).</summary>
        public static IEnumerable<string> ReadLines(string rel)
        {
            if (!TryReadAllText(rel, out var text) || string.IsNullOrEmpty(text)) yield break;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    yield return line;
            }
        }

        /// <summary>
        /// Files under <paramref name="relDir"/> matching a Directory.GetFiles-style pattern ("*.png",
        /// "c*.lua"; * and ?). Returns StreamingAssets-RELATIVE paths with forward slashes, ordinal-sorted.
        /// Empty (never null) when the folder is missing.
        /// </summary>
        public static string[] GetFiles(string relDir, string pattern = "*", bool recursive = false)
        {
            EnsureInit();
            var dir = Normalize(relDir);
            if (string.IsNullOrEmpty(pattern)) pattern = "*";
            var result = new List<string>();
            if (_archive)
            {
                lock (Gate)
                {
                    if (dir.Length > 0 && !Dirs.Contains(dir)) IndexSecondaryLocked();
                    if (dir.Length > 0 && !Dirs.Contains(dir)) return Array.Empty<string>();
                    var prefix = dir.Length == 0 ? "" : dir + "/";
                    foreach (var key in Files.Keys)
                    {
                        if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                        var rest = key.Substring(prefix.Length);
                        var slash = rest.LastIndexOf('/');
                        if (!recursive && slash >= 0) continue;
                        var name = slash >= 0 ? rest.Substring(slash + 1) : rest;
                        if (Glob(name, pattern)) result.Add(key);
                    }
                }
            }
            else
            {
                try
                {
                    var full = Full(dir);
                    if (!Directory.Exists(full)) return Array.Empty<string>();
                    var files = Directory.GetFiles(full, pattern,
                        recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                    foreach (var f in files)
                    {
                        var r = ToRelative(f);
                        if (r != null) result.Add(r);
                    }
                }
                finally { Pin(); }
            }

            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        /// <summary>
        /// URL for UnityWebRequest-only decoders (audio clips): file:// on desktop, the jar:file://…!/assets/…
        /// URL on Android (UnityWebRequest reads inside the APK itself).
        /// </summary>
        public static string UrlFor(string rel)
        {
            EnsureInit();
            var key = Normalize(rel);
            if (_archive) return _root.TrimEnd('/') + "/" + key;
            var full = Full(key);
            return full.Contains("://") ? full : "file://" + full;
        }

        /// <summary>Human-readable location for logs (a real path on desktop, "base.apk!/assets/…" on Android).</summary>
        public static string FullPathForLog(string rel)
        {
            EnsureInit();
            var key = Normalize(rel);
            if (!_archive) return Full(key);
            return (_primaryApk ?? "apk") + "!/assets/" + key;
        }

        /// <summary>
        /// A real file-system path for APIs that need one (SQLite opens a file by name). Desktop: the
        /// StreamingAssets file itself. Android: a copy under temporaryCachePath/StreamingFiles, refreshed
        /// when the build or entry size changes. Null when the entry is missing or the copy fails.
        /// </summary>
        public static string MaterializeToFile(string rel)
        {
            EnsureInit();
            var key = Normalize(rel);
            if (key.Length == 0) return null;
            if (!_archive)
            {
                var full = Full(key);
                try { return File.Exists(full) ? full : null; }
                finally { Pin(); }
            }

            lock (Gate)
            {
                if (!TryEntryLocked(key, out var e)) return null;
                var dest = Path.Combine(_cacheDir, key.Replace('/', Path.DirectorySeparatorChar));
                var stampPath = dest + ".stamp";
                var stamp = _buildStamp + "|" + e.Zip.Length + "|" + e.Zip.LastWriteTime.UtcTicks;
                try
                {
                    if (File.Exists(dest) && File.Exists(stampPath) &&
                        File.ReadAllText(stampPath) == stamp && new FileInfo(dest).Length == e.Zip.Length)
                        return dest;

                    var parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    var tmp = dest + ".tmp";
                    using (var src = e.Zip.Open())
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                        src.CopyTo(dst, 1 << 16);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmp, dest);
                    File.WriteAllText(stampPath, stamp);
                    Debug.Log($"{Tag} materialized {key} → {dest} ({e.Zip.Length} bytes)");
                    return dest;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{Tag} could not copy {key} out of the APK to {dest}: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Turn an absolute path under the StreamingAssets folder into the facade's relative form.
        /// Null when <paramref name="anyPath"/> is not under StreamingAssets.
        /// </summary>
        public static string ToRelative(string anyPath)
        {
            EnsureInit();
            if (string.IsNullOrEmpty(anyPath)) return null;
            var p = anyPath.Replace('\\', '/');
            var root = _root.Replace('\\', '/').TrimEnd('/');
            if (root.Length > 0 && p.StartsWith(root + "/", StringComparison.Ordinal))
                return Normalize(p.Substring(root.Length + 1));
            return null;
        }

        /// <summary>
        /// Lab self-test: lists a known folder and parses the card DB through the facade.
        /// Called from Desktop Lab (row + WRLDZ/Lab menu); works in an Android development build too.
        /// </summary>
        public static SelfTestReport SelfTest()
        {
            var report = new SelfTestReport();
            var sb = new StringBuilder();
            try
            {
                EnsureInit();
                sb.AppendLine("═══ WRLDZ STREAMING FILES SELF-TEST ═══");
                sb.AppendLine("mode   : " + (_archive ? "APK zip (" + _primaryApk + ")" : "System.IO"));
                sb.AppendLine("root   : " + _root);
                if (_archive)
                    lock (Gate)
                        sb.AppendLine($"index  : {Files.Count} files · {Dirs.Count} dirs · {Sources.Count} apk(s)");

                var decksDir = DirectoryExists("Decks");
                var decks = GetFiles("Decks", "*.json");
                sb.AppendLine($"Decks/ : exists={decksDir} · {decks.Length} *.json" +
                              (decks.Length > 0 ? " · first=" + decks[0] : ""));
                report.DeckFiles = decks.Length;

                var dbRel = "Cards/cards_db.json";
                var cards = 0;
                if (TryReadAllText(dbRel, out var json))
                {
                    var file = JsonUtility.FromJson<WRLDZ.Data.CardDatabaseFile>(json);
                    cards = file?.cards?.Length ?? 0;
                    sb.AppendLine($"card DB: {json.Length} chars · {cards} cards · {FullPathForLog(dbRel)}");
                }
                else
                {
                    sb.AppendLine("card DB: MISSING at " + FullPathForLog(dbRel));
                }
                report.Cards = cards;

                if (decks.Length > 0 && TryReadAllBytes(decks[0], out var deckBytes))
                    sb.AppendLine($"bytes  : {decks[0]} = {deckBytes.Length} bytes");

                var art = GetFiles("CardArt", "*.jpg");
                sb.AppendLine($"CardArt: {art.Length} *.jpg");
                sb.AppendLine("cdb    : OcgCore/cards.cdb exists=" + Exists("OcgCore/cards.cdb"));

                report.Ok = decksDir && decks.Length > 0 && cards > 0;
                sb.AppendLine(report.Ok ? "RESULT : PASS" : "RESULT : FAIL");
            }
            catch (Exception ex)
            {
                report.Ok = false;
                sb.AppendLine("RESULT : FAIL · " + ex.GetType().Name + ": " + ex.Message);
            }

            report.Summary = sb.ToString();
            if (report.Ok) Debug.Log(Tag + "\n" + report.Summary);
            else Debug.LogError(Tag + "\n" + report.Summary);
            return report;
        }

        public sealed class SelfTestReport
        {
            public bool Ok;
            public int Cards;
            public int DeckFiles;
            public string Summary;

            public string OneLine =>
                (Ok ? "Streaming PASS" : "Streaming FAIL") +
                $" · {Cards} cards · {DeckFiles} decks · " + (IsArchive ? "APK zip" : "System.IO");
        }

        /// <summary>
        /// Forward slashes, no leading slash, "." removed, ".." resolved inside the root (never above it).
        /// An absolute path under StreamingAssets is accepted and made relative.
        /// </summary>
        public static string Normalize(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return "";
            EnsureInit();
            var p = rel.Replace('\\', '/');
            if (p.IndexOf(':') >= 0 || p.StartsWith("/", StringComparison.Ordinal))
            {
                var root = _root.Replace('\\', '/').TrimEnd('/');
                if (root.Length > 0 && p.StartsWith(root + "/", StringComparison.Ordinal))
                    p = p.Substring(root.Length + 1);
                else if (root.Length > 0 && p == root)
                    return "";
            }

            if (p.IndexOf("//", StringComparison.Ordinal) < 0 && p.IndexOf("./", StringComparison.Ordinal) < 0 &&
                !p.StartsWith("/", StringComparison.Ordinal) && !p.EndsWith("/", StringComparison.Ordinal) &&
                p != "." && p != "..")
                return p;

            var parts = new List<string>();
            foreach (var seg in p.Split('/'))
            {
                if (seg.Length == 0 || seg == ".") continue;
                if (seg == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(seg);
            }
            return string.Join("/", parts);
        }

        // ─────────────────────────────── internals ───────────────────────────────

        static string Full(string key) =>
            key.Length == 0 ? _root : Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>
        /// Linux Editor: File.Exists / GetFiles can native-chdir and Bee then fatals; re-pin after each
        /// desktop IO call (no-op outside the Editor). Main thread only — Pin touches Application.dataPath.
        /// </summary>
        static void Pin()
        {
#if UNITY_EDITOR
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId)
                EditorWorkingDirectory.Pin();
#endif
        }

        static void EnsureInit()
        {
            if (_init) return;
            lock (Gate)
            {
                if (_init) return;
                if (_mainThreadId < 0) _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                _root = Application.streamingAssetsPath ?? "";
                _dataPath = Application.dataPath ?? "";
                _archive = _root.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);
                if (_archive)
                {
                    _cacheDir = Path.Combine(Application.temporaryCachePath, "StreamingFiles");
                    _buildStamp = Application.version + "/" + Application.buildGUID;
                    _primaryApk = ApkPathFromJar(_root) ?? _dataPath;
                    IndexApkLocked(_primaryApk, logFailure: true);
                    var n = Files.Count;
                    if (n == 0)
                    {
                        // Nothing under assets/ in the base APK (asset-pack install): pull the splits in now.
                        IndexSecondaryLocked();
                    }
                    Debug.Log($"{Tag} Android APK mode · {Files.Count} files / {Dirs.Count} dirs indexed from " +
                              $"{Sources.Count} apk(s) · primary {_primaryApk}");
                }
                _init = true;
            }
        }

        static string ApkPathFromJar(string jar)
        {
            // jar:file:///data/app/…/base.apk!/assets
            var bang = jar.IndexOf("!/", StringComparison.Ordinal);
            var body = bang >= 0 ? jar.Substring(0, bang) : jar;
            if (body.StartsWith("jar:", StringComparison.OrdinalIgnoreCase)) body = body.Substring(4);
            if (body.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) body = body.Substring(7);
            return string.IsNullOrEmpty(body) ? null : Uri.UnescapeDataString(body);
        }

        static void IndexApkLocked(string apkPath, bool logFailure)
        {
            if (string.IsNullOrEmpty(apkPath)) return;
            foreach (var s in Sources)
                if (string.Equals(s.Path, apkPath, StringComparison.Ordinal))
                    return;

            FileStream fs = null;
            try
            {
                fs = new FileStream(apkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
                var src = new ApkSource { Path = apkPath, Stream = fs, Zip = zip };
                Sources.Add(src);
                foreach (var z in zip.Entries)
                {
                    var name = z.FullName;
                    if (!name.StartsWith(ApkAssetsPrefix, StringComparison.Ordinal)) continue;
                    var rel = name.Substring(ApkAssetsPrefix.Length);
                    if (rel.Length == 0 || rel.StartsWith(ApkPlayerDataPrefix, StringComparison.Ordinal)) continue;
                    if (rel.EndsWith("/", StringComparison.Ordinal))
                    {
                        AddDirsLocked(rel.TrimEnd('/'), includeSelf: true);
                        continue;
                    }
                    if (Files.ContainsKey(rel)) continue; // earlier (base) APK wins
                    Files[rel] = new Entry { Source = src, Zip = z };
                    AddDirsLocked(rel, includeSelf: false);
                }
            }
            catch (Exception ex)
            {
                fs?.Dispose();
                if (logFailure && !_loggedOpenFailure)
                {
                    _loggedOpenFailure = true;
                    Debug.LogError($"{Tag} cannot open APK '{apkPath}' as a zip ({ex.GetType().Name}: {ex.Message}). " +
                                   "Every StreamingAssets read (card DB, decks, art, models) will come back missing.");
                }
            }
        }

        static void AddDirsLocked(string relPath, bool includeSelf)
        {
            var p = includeSelf ? relPath : ParentOf(relPath);
            while (!string.IsNullOrEmpty(p) && Dirs.Add(p))
                p = ParentOf(p);
        }

        static string ParentOf(string rel)
        {
            var i = rel.LastIndexOf('/');
            return i > 0 ? rel.Substring(0, i) : "";
        }

        /// <summary>Index Application.dataPath and split APKs beside the base APK (once).</summary>
        static void IndexSecondaryLocked()
        {
            if (!_archive || _secondaryIndexed) return;
            _secondaryIndexed = true;
            var before = Files.Count;
            if (!string.Equals(_dataPath, _primaryApk, StringComparison.Ordinal))
                IndexApkLocked(_dataPath, logFailure: false);
            try
            {
                var dir = Path.GetDirectoryName(_primaryApk);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var splits = Directory.GetFiles(dir, "*.apk");
                    Array.Sort(splits, StringComparer.Ordinal);
                    foreach (var apk in splits)
                        IndexApkLocked(apk, logFailure: false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{Tag} could not list split APKs beside {_primaryApk}: {ex.Message}");
            }

            if (Files.Count != before)
                Debug.Log($"{Tag} split/base APKs added {Files.Count - before} entries ({Sources.Count} apk(s)).");
        }

        static bool TryEntry(string key, out Entry e)
        {
            lock (Gate) return TryEntryLocked(key, out e);
        }

        static bool TryEntryLocked(string key, out Entry e)
        {
            if (Files.TryGetValue(key, out e)) return true;
            if (!_secondaryIndexed)
            {
                IndexSecondaryLocked();
                if (Files.TryGetValue(key, out e)) return true;
            }

            if (!_loggedMiss)
            {
                _loggedMiss = true;
                Debug.LogWarning($"{Tag} first miss: '{key}' is not in the APK ({Files.Count} StreamingAssets entries " +
                                 $"indexed from {Sources.Count} apk(s)). Further misses are silent; loaders log their own.");
            }
            return false;
        }

        static byte[] ReadEntryLocked(Entry e)
        {
            try
            {
                var len = e.Zip.Length;
                if (len > int.MaxValue) throw new IOException("entry larger than 2 GB");
                var buf = new byte[len];
                using (var s = e.Zip.Open())
                {
                    var off = 0;
                    while (off < buf.Length)
                    {
                        var n = s.Read(buf, off, buf.Length - off);
                        if (n <= 0) break;
                        off += n;
                    }
                    if (off != buf.Length)
                        throw new EndOfStreamException($"read {off} of {buf.Length} bytes");
                }
                return buf;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{Tag} read failed for {e.Zip.FullName} in {e.Source.Path}: {ex.Message}");
                return null;
            }
        }

        static string Decode(byte[] bytes)
        {
            // Same result as File.ReadAllText: UTF-8 default, BOM (UTF-8/16/32) detected.
            using (var ms = new MemoryStream(bytes, writable: false))
            using (var r = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                return r.ReadToEnd();
        }

        static void RememberTextLocked(string key, string text)
        {
            var node = TextLru.AddFirst(new KeyValuePair<string, string>(key, text));
            TextCache[key] = node;
            while (TextLru.Count > TextCacheCapacity)
            {
                var last = TextLru.Last;
                TextLru.RemoveLast();
                TextCache.Remove(last.Value.Key);
            }
        }

        /// <summary>Directory.GetFiles-style wildcard (* and ?), case-insensitive on the file name.</summary>
        static bool Glob(string name, string pattern)
        {
            if (pattern == "*" || pattern == "*.*") return true;
            int n = 0, p = 0, starP = -1, starN = 0;
            while (n < name.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' ||
                                           char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(name[n])))
                {
                    n++;
                    p++;
                }
                else if (p < pattern.Length && pattern[p] == '*')
                {
                    starP = p++;
                    starN = n;
                }
                else if (starP >= 0)
                {
                    p = starP + 1;
                    n = ++starN;
                }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
    }
}
