using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using WRLDZ.Duel.Ocg;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Bulk official library integrity. Proves every official filename resolves and
    /// the native core can LoadScript/NewCard them. Lab-deck preflight is not enough.
    /// </summary>
    public static class OcgOfficialLibraryTests
    {
        const int Batch = 200;
        const uint ExtraTypeBits = 0x40u | 0x2000u | 0x800000u | 0x4000000u;
        static readonly Regex PasscodeFile = new Regex(@"^c(\d+)\.lua$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [MenuItem("WRLDZ/Lab/Run OCG Official Library Integrity")]
        public static void RunInteractive()
        {
            var r = Run();
            EditorUtility.DisplayDialog(r.Ok ? "OCG Library PASS" : "OCG Library FAIL", r.Summary, "OK");
        }

        public struct Report
        {
            public bool Ok;
            public string Summary;
        }

        public static Report Run()
        {
            try
            {
                AssertIndexResolvesOfficialTree();
                AssertNativeSampleExtraTypes();
                AssertNativeLoadsOfficialLibrary();
                return new Report { Ok = true, Summary = "official library resolvable and loadable" };
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return new Report { Ok = false, Summary = ex.Message };
            }
        }

        public static void AssertNativeSampleExtraTypes()
        {
            if (!OcgNative.TryLoad(out var err))
                throw new Exception(err);
            var extra = new[] { 85684223, 44508094, 84013237, 1861629 };
            using (var core = new NativeOcgDuelCore())
            {
                core.CreateDuel(1, new OcgDuelStartInfo
                {
                    Seed = new uint[] { 1, 0, 0, 0 },
                    PlayerMain = Array.Empty<int>(),
                    OpponentMain = Array.Empty<int>(),
                    PlayerExtra = extra,
                    OpponentExtra = extra,
                    Start = false
                });
                if (core.QueryCount(0, (uint)OcgLocation.Extra) != 4 ||
                    core.QueryCount(1, (uint)OcgLocation.Extra) != 4)
                    throw new Exception("extra-type NewCard QueryCount");
                if (core.ScriptReadFail > 0)
                    throw new Exception("extra-type scriptReader misses " + core.ScriptReadFail);
                if (core.ScriptLoadZero > 0)
                    throw new Exception("extra-type OCG_LoadScript returned 0 (" + core.ScriptLoadZero + ")");
            }
        }

        public static void AssertIndexResolvesOfficialTree()
        {
            OcgScriptStore.Invalidate();
            if (!string.IsNullOrEmpty(OcgScriptStore.IndexError))
                throw new Exception("script index: " + OcgScriptStore.IndexError);
            if (OcgScriptStore.IndexedCount < 10000)
                throw new Exception("indexed " + OcgScriptStore.IndexedCount + " < 10000");

            foreach (var name in new[] { "constant.lua", "utility.lua", "procedure.lua", "chain.lua", "proc_fusion.lua" })
            {
                if (!OcgScriptStore.TryRead(name, out var bytes, out var err) || bytes == null || bytes.Length == 0)
                    throw new Exception("root lib missing " + name + " " + err);
            }

            if (OcgScriptStore.TryRead("../utility.lua", out _, out var trav) == false &&
                (trav == null || trav.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) < 0))
                throw new Exception("traversal must be rejected, got " + trav);
            if (OcgScriptStore.TryRead("official/c85684223.lua", out _, out var nested))
                throw new Exception("pathy names must be rejected, got " + nested);

            var official = Path.Combine(OcgScriptStore.ScriptsDir, "official");
            if (!Directory.Exists(official))
                throw new Exception("missing scripts/official");
            var files = Directory.GetFiles(official, "*.lua", SearchOption.AllDirectories);
            if (files.Length < 10000)
                throw new Exception("official tree " + files.Length + " < 10000");
            var missing = 0;
            string firstMissing = null;
            foreach (var f in files)
            {
                var name = Path.GetFileName(f);
                if (!OcgScriptStore.TryRead(name, out _, out var err))
                {
                    missing++;
                    if (firstMissing == null) firstMissing = name + " " + err;
                }
            }
            var rootLua = Directory.GetFiles(OcgScriptStore.ScriptsDir, "*.lua", SearchOption.TopDirectoryOnly);
            foreach (var f in rootLua)
            {
                var name = Path.GetFileName(f);
                if (!OcgScriptStore.TryRead(name, out _, out var err))
                {
                    missing++;
                    if (firstMissing == null) firstMissing = name + " " + err;
                }
            }
            if (missing > 0)
                throw new Exception("unresolved official filenames " + missing + " first=" + firstMissing);

            if (!OcgScriptStore.TryRead("c85684223.lua", out var reaper, out var rerr) || reaper == null)
                throw new Exception("Reaper script not indexed: " + rerr);
        }

        public static void AssertNativeLoadsOfficialLibrary()
        {
            if (!OcgNative.TryLoad(out var err))
                throw new Exception(err);
            if (!OcgCardCatalog.SqliteReady)
                throw new Exception("sqlite/CDB required for official library smoke");

            var official = Path.Combine(OcgScriptStore.ScriptsDir, "official");
            var files = Directory.GetFiles(official, "c*.lua", SearchOption.AllDirectories);
            var codes = new List<int>(files.Length);
            foreach (var f in files)
            {
                var m = PasscodeFile.Match(Path.GetFileName(f));
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups[1].Value, out var id) || id <= 0) continue;
                if (!OcgCardCatalog.TryGetFromSqlite((uint)id, out var data) || data == null || data.Code == 0)
                    continue;
                codes.Add(id);
            }
            if (codes.Count < 1000)
                throw new Exception("CDB-backed official codes " + codes.Count + " < 1000");

            var loadFail = 0;
            string loadFirst = null;
            var newFail = 0;
            string newFirst = null;
            for (var off = 0; off < codes.Count; off += Batch)
            {
                var n = Math.Min(Batch, codes.Count - off);
                var main = new List<int>(n);
                var extra = new List<int>();
                for (var i = 0; i < n; i++)
                {
                    var id = codes[off + i];
                    if (OcgCardCatalog.TryGetFromSqlite((uint)id, out var data) && data != null &&
                        (data.Type & ExtraTypeBits) != 0)
                        extra.Add(id);
                    else
                        main.Add(id);
                }
                using (var core = new NativeOcgDuelCore())
                {
                    core.CreateDuel(1, new OcgDuelStartInfo
                    {
                        Seed = new uint[] { 1, 0, 0, 0 },
                        PlayerMain = main.ToArray(),
                        OpponentMain = Array.Empty<int>(),
                        PlayerExtra = extra.ToArray(),
                        OpponentExtra = Array.Empty<int>(),
                        Start = false
                    });
                    var got = core.QueryCount(0, (uint)OcgLocation.Deck) +
                              core.QueryCount(0, (uint)OcgLocation.Extra);
                    if (got != (uint)n)
                    {
                        newFail++;
                        if (newFirst == null)
                            newFirst = "QueryCount " + got + " want " + n + " at offset " + off;
                    }
                    if (core.ScriptReadFail > 0)
                    {
                        loadFail += core.ScriptReadFail;
                        if (loadFirst == null) loadFirst = "scriptReader miss batch @" + off;
                    }
                    if (core.ScriptLoadZero > 0)
                    {
                        loadFail += core.ScriptLoadZero;
                        if (loadFirst == null) loadFirst = "LoadScript 0 batch @" + off;
                    }
                }
            }

            if (newFail > 0)
                throw new Exception("official NewCard QueryCount failures " + newFail + " first=" + newFirst);
            if (loadFail > 0)
                throw new Exception("official LoadScript failures " + loadFail + " first=" + loadFirst);
            Debug.Log("[WRLDZ OCG] official library smoke codes=" + codes.Count + " batches=" +
                      ((codes.Count + Batch - 1) / Batch));
        }
    }
}
