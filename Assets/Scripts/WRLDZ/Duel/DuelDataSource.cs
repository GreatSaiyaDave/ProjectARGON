using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Read-only rules data (R5): card catalog, decks, TextFX seed, ERAZ eras / text snapshots, YGOPro
    /// trigger + continuous catalogs, TCG pool, banlist, AI heuristics. Paths are StreamingAssets-relative
    /// ("Cards/cards_db.json"). Each catalog reads its bytes ONCE, builds its tables, then only reads them
    /// (thread-safe publication), so many matches can share one process.
    /// Unity default: <see cref="StreamingFilesDataSource"/> (the same StreamingFiles reads as before).
    /// Server / headless: <see cref="DirectoryDataSource"/> or <see cref="InMemoryDataSource"/> set through
    /// <see cref="DuelData.Source"/> at boot, before the first duel.
    /// </summary>
    public interface IDuelDataSource
    {
        bool TryReadText(string relativePath, out string text);
        bool TryReadBytes(string relativePath, out byte[] bytes);
        bool Exists(string relativePath);

        /// <summary>
        /// Change stamp of a file (desktop hot edit reload), or null when unknown. An immutable bundle
        /// returns a constant, so the card DB is parsed once.
        /// </summary>
        string Stamp(string relativePath);

        /// <summary>Human-readable location for logs.</summary>
        string Describe(string relativePath);

        /// <summary>Writable folder for learned caches (compiled effects); null = keep them in memory only.</summary>
        string CacheDirectory { get; }
    }

    /// <summary>Process-wide rules data source. Set once at boot; defaults to StreamingAssets.</summary>
    public static class DuelData
    {
        static IDuelDataSource _source;

        public static IDuelDataSource Source
        {
            get
            {
                var s = Volatile.Read(ref _source);
                if (s != null) return s;
                Interlocked.CompareExchange(ref _source, new StreamingFilesDataSource(), null);
                return Volatile.Read(ref _source);
            }
            // Catalogs never re-read, so swap sources before the first load (boot, tests).
            set => Volatile.Write(ref _source, value);
        }

        public static bool TryReadText(string relativePath, out string text) =>
            Source.TryReadText(relativePath, out text);

        public static bool TryReadBytes(string relativePath, out byte[] bytes) =>
            Source.TryReadBytes(relativePath, out bytes);

        public static bool Exists(string relativePath) => Source.Exists(relativePath);

        public static string Describe(string relativePath) => Source.Describe(relativePath);
    }

    /// <summary>Unity default: StreamingAssets through <see cref="StreamingFiles"/> (APK-safe, thread-safe).</summary>
    public sealed class StreamingFilesDataSource : IDuelDataSource
    {
        public bool TryReadText(string relativePath, out string text) =>
            StreamingFiles.TryReadAllText(relativePath, out text);

        public bool TryReadBytes(string relativePath, out byte[] bytes) =>
            StreamingFiles.TryReadAllBytes(relativePath, out bytes);

        public bool Exists(string relativePath) => StreamingFiles.Exists(relativePath);

        public string Stamp(string relativePath)
        {
            // Through the facade so the desktop stat re-pins the Linux Editor cwd (AI-09) like every other read.
            try
            {
                if (!StreamingFiles.TryGetStamp(relativePath, out var length, out var ticks)) return null;
                return StreamingFiles.IsArchive ? "apk:" + length : length + ":" + ticks;
            }
            catch
            {
                return null;
            }
        }

        public string Describe(string relativePath) => StreamingFiles.FullPathForLog(relativePath);

        public string CacheDirectory => Path.Combine(Application.persistentDataPath, "WRLDZ");
    }

    /// <summary>A StreamingAssets-shaped folder on disk (server bundle, headless harness). Immutable.</summary>
    public sealed class DirectoryDataSource : IDuelDataSource
    {
        readonly string _root;

        /// <param name="root">Folder laid out like StreamingAssets (Cards/, Decks/, WRLDZ/, OcgCore/ …).</param>
        /// <param name="cacheDirectory">Writable folder for learned caches, or null for memory only.</param>
        public DirectoryDataSource(string root, string cacheDirectory = null)
        {
            _root = Path.GetFullPath(root ?? ".");
            CacheDirectory = cacheDirectory;
        }

        string Full(string rel) =>
            Path.Combine(_root, (rel ?? "").Replace('\\', '/').TrimStart('/'));

        public bool TryReadText(string relativePath, out string text)
        {
            text = null;
            var p = Full(relativePath);
            if (!File.Exists(p)) return false;
            text = File.ReadAllText(p, Encoding.UTF8);
            return true;
        }

        public bool TryReadBytes(string relativePath, out byte[] bytes)
        {
            bytes = null;
            var p = Full(relativePath);
            if (!File.Exists(p)) return false;
            bytes = File.ReadAllBytes(p);
            return true;
        }

        public bool Exists(string relativePath) => File.Exists(Full(relativePath));
        public string Stamp(string relativePath) => Exists(relativePath) ? "immutable" : null;
        public string Describe(string relativePath) => Full(relativePath);
        public string CacheDirectory { get; }
    }

    /// <summary>Rules data already in memory (a server's versioned bundle): relative path → bytes. Immutable.</summary>
    public sealed class InMemoryDataSource : IDuelDataSource
    {
        readonly Dictionary<string, byte[]> _files;
        readonly string _label;

        public InMemoryDataSource(IDictionary<string, byte[]> files, string label = "memory")
        {
            _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (files != null)
                foreach (var kv in files)
                    _files[Key(kv.Key)] = kv.Value;
            _label = label;
        }

        static string Key(string rel) => (rel ?? "").Replace('\\', '/').TrimStart('/');

        public bool TryReadText(string relativePath, out string text)
        {
            text = null;
            if (!_files.TryGetValue(Key(relativePath), out var b) || b == null) return false;
            // UTF-8 with or without BOM, like File.ReadAllText.
            text = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF
                ? Encoding.UTF8.GetString(b, 3, b.Length - 3)
                : Encoding.UTF8.GetString(b);
            return true;
        }

        public bool TryReadBytes(string relativePath, out byte[] bytes) =>
            _files.TryGetValue(Key(relativePath), out bytes) && bytes != null;

        public bool Exists(string relativePath) => _files.ContainsKey(Key(relativePath));
        public string Stamp(string relativePath) => Exists(relativePath) ? "immutable" : null;
        public string Describe(string relativePath) => _label + ":" + Key(relativePath);
        public string CacheDirectory => null;
    }
}
