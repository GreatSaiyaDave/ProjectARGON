using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>Runtime gate: native only if last preflight ok and fingerprint still matches.</summary>
    public static class OcgPreflightState
    {
        public const string ResultRelative = "Library/OcgNativePreflight.last.json";

        [Serializable]
        public class ResultFile
        {
            public bool ok;
            public int major;
            public int minor;
            public bool idleSeen;
            public string preflightFingerprint;
            public string reason;
        }

        public static bool TryUseNative(out string reason)
        {
            reason = null;
            var path = ResultPath();
            if (!File.Exists(path))
            {
                reason = "no preflight result";
                return false;
            }
            ResultFile r;
            try { r = JsonUtility.FromJson<ResultFile>(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                reason = "preflight parse: " + ex.Message;
                return false;
            }
            if (r == null || !r.ok || !r.idleSeen)
            {
                reason = string.IsNullOrEmpty(r?.reason) ? "preflight not ok" : r.reason;
                return false;
            }
            var fp = ComputeFingerprint(out _, out _);
            if (!string.Equals(fp, r.preflightFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                reason = "preflight fingerprint stale";
                return false;
            }
            if (!OcgNative.TryLoad(out var err))
            {
                reason = err;
                return false;
            }
            if (!OcgCardCatalog.SqliteReady)
            {
                reason = "sqlite/CDB required for full_official_pool native";
                return false;
            }
            return true;
        }

        public static string ResultPath()
        {
            var root = Directory.GetParent(Application.dataPath);
            return Path.Combine(root != null ? root.FullName : Application.dataPath, ResultRelative);
        }

        public static string ComputeFingerprint(out int major, out int minor)
        {
            major = 0;
            minor = 0;
            try { OcgNativeApi.OCG_GetVersion(out major, out minor); }
            catch { /* plugin missing */ }

            using (var sha = SHA256.Create())
            {
                void AddFile(string path)
                {
                    var bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
                    var n = Encoding.UTF8.GetBytes(Path.GetFileName(path) + "\n");
                    sha.TransformBlock(n, 0, n.Length, null, 0);
                    sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }

                AddFile(OcgNative.ResolvePluginFile());
                AddFile(Path.Combine(Application.streamingAssetsPath, "OcgCore", "cards.cdb"));
                var commit = Encoding.UTF8.GetBytes("scriptsCommit=" + ScriptsCommit() + "\n");
                sha.TransformBlock(commit, 0, commit.Length, null, 0);
                AddFile(Path.Combine(Application.streamingAssetsPath, "OcgCore", "lab_card_manifest.json"));
                AddFile(Path.Combine(Application.streamingAssetsPath, "Decks", "ocg_lab_player.json"));
                AddFile(Path.Combine(Application.streamingAssetsPath, "Decks", "ocg_lab_ai.json"));
                var ver = Encoding.UTF8.GetBytes("major=" + major + ",minor=" + minor);
                sha.TransformFinalBlock(ver, 0, ver.Length);
                var h = sha.Hash;
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static string ScriptsCommit()
        {
            var p = Path.Combine(Application.streamingAssetsPath, "OcgCore", "PROVENANCE.json");
            if (!File.Exists(p)) return "";
            var json = File.ReadAllText(p);
            const string key = "\"git\"";
            var i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";
            var q1 = json.IndexOf('"', json.IndexOf(':', i) + 1);
            var q2 = json.IndexOf('"', q1 + 1);
            if (q1 < 0 || q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }
    }
}
