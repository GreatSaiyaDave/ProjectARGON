using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Frozen Original-pool snapshot for Path B (staged supported pool).
    /// Agents read the JSON/MD instead of re-classifying 1411 cards.
    /// </summary>
    public static class OriginalPoolStatusMenu
    {
        public const string RelJson = "Assets/StreamingAssets/WRLDZ/eras/original_status.json";
        public const string RelMd = "Assets/StreamingAssets/WRLDZ/eras/original_status.md";
        public const string RelSupported = "Assets/StreamingAssets/WRLDZ/eras/supported_original.json";

        [Serializable]
        public class Totals
        {
            public int total;
            public int structural;
            public int implemented;
            public int stub;
            public int unimplemented;
            public int missing;
            public float playablePct;
        }

        [Serializable]
        public class SetRow
        {
            public string code;
            public string name;
            public int order;
            public string role;
            public int n;
            public int structural;
            public int implemented;
            public int stub;
            public int unimplemented;
            public int missing;
            public float playablePct;
        }

        [Serializable]
        public class BlockedCard
        {
            public int id;
            public string name;
            public string type;
            public string set;
            public string status;
            public string leftover;
            public string descHead;
        }

        [Serializable]
        public class StatusFile
        {
            public string generatedUtc;
            public int compilerVersion;
            public string productPath = "B-staged";
            public string invariant = "total = structural + implemented + stub + unimplemented";
            public bool isReleased;
            public Totals original = new Totals();
            public SetRow[] sets = Array.Empty<SetRow>();
            public int[] readyIds = Array.Empty<int>();
            public BlockedCard[] blocked = Array.Empty<BlockedCard>();
            public BlockedCard[] lobGaps = Array.Empty<BlockedCard>();
        }

        [Serializable]
        public class SupportedFile
        {
            public string generatedUtc;
            public int compilerVersion;
            public string eraId = ErazFormat.Original;
            public int[] ids = Array.Empty<int>();
        }

        [MenuItem("WRLDZ/Rules/Export Original Pool Status")]
        public static void ExportFromMenu()
        {
            var md = Export();
            Debug.Log("[WRLDZ Original status]\n" + md);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Original pool status", Truncate(md, 1400), "OK");
        }

        [CliCommand("wrldz_original_status",
            "Classify Original pre-TLM pool; write original_status.json/md + supported_original.json.",
            MainThreadRequired = true)]
        public static string ExportCli() => Export();

        public static string Export()
        {
            WRLDZ.Core.EditorWorkingDirectory.Pin();
            var db = CardDatabase.Instance ?? CardDatabase.Load();
            var file = CardEraCurriculum.Load();
            var snap = Build(db, file);
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var jsonPath = Path.Combine(root, RelJson);
            var mdPath = Path.Combine(root, RelMd);
            var supPath = Path.Combine(root, RelSupported);
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath) ?? root);
            var prev = jsonPath.Replace(".json", ".prev.json");
            if (File.Exists(jsonPath))
                File.Copy(jsonPath, prev, true);
            File.WriteAllText(jsonPath, JsonUtility.ToJson(snap, true));
            var md = ToMarkdown(snap);
            File.WriteAllText(mdPath, md);
            File.WriteAllText(supPath, JsonUtility.ToJson(new SupportedFile
            {
                generatedUtc = snap.generatedUtc,
                compilerVersion = snap.compilerVersion,
                ids = snap.readyIds
            }, true));
            AssetDatabase.Refresh();
            return md;
        }

        static StatusFile Build(CardDatabase db, CardEraCurriculum.SetFile file)
        {
            var snap = new StatusFile
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                compilerVersion = CardTextEffectCompiler.Version,
                isReleased = ErazFormat.IsReleased(ErazFormat.Original)
            };
            if (file?.sets == null || db == null)
                return snap;

            var setRows = new List<SetRow>();
            var blocked = new List<BlockedCard>();
            var lobGaps = new List<BlockedCard>();
            var ready = new List<int>();
            var seenOrig = new HashSet<int>();
            var orig = new Totals();

            var sets = (CardEraCurriculum.SetDef[])file.sets.Clone();
            Array.Sort(sets, (a, b) => a.order.CompareTo(b.order));
            foreach (var s in sets)
            {
                if (s == null) continue;
                var row = new SetRow
                {
                    code = s.code,
                    name = s.name,
                    order = s.order,
                    role = s.role,
                    n = s.priorityPasscodes?.Length ?? 0
                };
                var ids = s.priorityPasscodes ?? Array.Empty<int>();
                var origSet = s.order < 15;
                foreach (var id in ids)
                {
                    if (origSet) seenOrig.Add(id);
                    var def = db.Get(id);
                    if (def == null)
                    {
                        row.missing++;
                        if (origSet) orig.missing++;
                        continue;
                    }

                    var kind = CardEffectStatus.Classify(def);
                    if (kind == CardEffectStatusKind.Structural)
                    {
                        row.structural++;
                        if (origSet)
                        {
                            orig.structural++;
                            ready.Add(id);
                        }
                    }
                    else if (kind == CardEffectStatusKind.Implemented)
                    {
                        row.implemented++;
                        if (origSet)
                        {
                            orig.implemented++;
                            ready.Add(id);
                        }
                    }
                    else
                    {
                        var card = MakeBlocked(def, s.code, kind);
                        if (kind == CardEffectStatusKind.Stub)
                        {
                            row.stub++;
                            if (origSet) orig.stub++;
                        }
                        else
                        {
                            row.unimplemented++;
                            if (origSet) orig.unimplemented++;
                        }

                        if (origSet)
                        {
                            blocked.Add(card);
                            if (s.order == 1)
                                lobGaps.Add(card);
                        }
                    }
                }

                row.playablePct = row.n == 0 ? 0 : 100f * (row.structural + row.implemented) / row.n;
                setRows.Add(row);
            }

            orig.total = seenOrig.Count;
            orig.playablePct = orig.total == 0
                ? 0
                : 100f * (orig.structural + orig.implemented) / orig.total;
            snap.original = orig;
            snap.sets = setRows.ToArray();
            snap.readyIds = ready.ToArray();
            snap.blocked = blocked.ToArray();
            snap.lobGaps = lobGaps.ToArray();
            return snap;
        }

        static BlockedCard MakeBlocked(CardDef def, string set, CardEffectStatusKind kind)
        {
            var leftover = "";
            var prog = CardTextEffectCompiler.Compile(def);
            if (prog?.UnparsedFragments != null && prog.UnparsedFragments.Length > 0)
                leftover = prog.UnparsedFragments[0] ?? "";
            if (leftover.Length > 160)
                leftover = leftover.Substring(0, 160);
            var head = def.desc ?? "";
            if (head.Length > 160)
                head = head.Substring(0, 160);
            return new BlockedCard
            {
                id = def.id,
                name = def.name,
                type = def.type,
                set = set,
                status = kind == CardEffectStatusKind.Stub ? "stub" : "unimplemented",
                leftover = leftover,
                descHead = head
            };
        }

        static string ToMarkdown(StatusFile s)
        {
            var o = s.original ?? new Totals();
            var sb = new StringBuilder();
            sb.AppendLine("# Original pool status");
            sb.AppendLine();
            sb.AppendLine("Agents: read this file. Do **not** re-audit architecture or re-classify 1411 cards.");
            sb.AppendLine("Product path: **B-staged** (supported pool = Structural + Implemented).");
            sb.AppendLine("Link later: same model on the `vrains` band; C# is the only live authority.");
            sb.AppendLine();
            sb.AppendLine("| Field | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| generatedUtc | {s.generatedUtc} |");
            sb.AppendLine($"| compilerVersion | {s.compilerVersion} |");
            sb.AppendLine($"| IsReleased(original) | {s.isReleased} |");
            sb.AppendLine($"| Original total | {o.total} |");
            sb.AppendLine($"| Structural | {o.structural} |");
            sb.AppendLine($"| Implemented | {o.implemented} |");
            sb.AppendLine($"| Stub | {o.stub} |");
            sb.AppendLine($"| Unimplemented | {o.unimplemented} |");
            sb.AppendLine($"| Ready | {o.structural + o.implemented} ({o.playablePct:0.0}%) |");
            sb.AppendLine($"| Blocking | {o.stub + o.unimplemented} |");
            sb.AppendLine($"| LOB gaps | {(s.lobGaps == null ? 0 : s.lobGaps.Length)} |");
            sb.AppendLine();
            sb.AppendLine("Invariant: `total = structural + implemented + stub + unimplemented`.");
            sb.AppendLine();
            sb.AppendLine("## Sets");
            sb.AppendLine();
            sb.AppendLine("| Set | n | struct | impl | stub | unimpl | playable% |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            if (s.sets != null)
            {
                foreach (var r in s.sets)
                {
                    if (r == null) continue;
                    sb.AppendLine(
                        $"| {r.code} | {r.n} | {r.structural} | {r.implemented} | {r.stub} | {r.unimplemented} | {r.playablePct:0.0} |");
                }
            }

            if (s.lobGaps != null && s.lobGaps.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## LOB remaining (demo slice)");
                sb.AppendLine();
                foreach (var c in s.lobGaps)
                {
                    if (c == null) continue;
                    sb.AppendLine($"- `{c.status}` {c.id} {c.name} ({c.type})");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Refresh: `Tools/original-pool-report.sh`");
            sb.AppendLine("Capability slice: `/wrldz-original-capability` with `args.capability`.");
            return sb.ToString();
        }

        static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= n) return s;
            return s.Substring(0, n) + "…";
        }
    }
}
