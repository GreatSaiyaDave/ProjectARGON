using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Pre-Link set curriculum (LOB → MRD → SRL …). Measures compile/registry coverage
    /// and drives AI mastery training for the next sets.
    /// </summary>
    public static class CardEraCurriculum
    {
        [Serializable]
        public class SetFile
        {
            public int version;
            public string preLinkWall;
            public SetDef[] sets;
        }

        [Serializable]
        public class SetDef
        {
            public string code;
            public string name;
            public int order;
            public string role;
            public string notes;
            public int[] priorityPasscodes;
        }

        public struct SetCoverage
        {
            public string Code;
            public string Name;
            public int PriorityIds;
            public int InDatabase;
            public int NormalNoEffect;
            public int FullyCompiled;
            public int PartialCompiled;
            public int RegistryScripted;
            public int MissingFromDb;
            public string Report;
        }

        static SetFile _cached;

        public static SetFile Load()
        {
            if (_cached != null) return _cached;
            var path = Path.Combine(Application.streamingAssetsPath, "WRLDZ", "eras", "pre_link_sets.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[WRLDZ Era] Missing pre_link_sets.json");
                return new SetFile { sets = Array.Empty<SetDef>() };
            }

            try
            {
                _cached = JsonUtility.FromJson<SetFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Era] Parse failed: " + ex.Message);
                _cached = new SetFile { sets = Array.Empty<SetDef>() };
            }

            return _cached;
        }

        public static IEnumerable<SetDef> NextTwoSets()
        {
            var file = Load();
            if (file?.sets == null) yield break;
            foreach (var s in file.sets.OrderBy(x => x.order))
            {
                if (s.role == "next_set_1" || s.role == "next_set_2" || s.order <= 3)
                    yield return s;
            }
        }

        public static SetCoverage Measure(SetDef set, CardDatabase db = null)
        {
            db ??= CardDatabase.Instance ?? CardDatabase.Load();
            var cov = new SetCoverage
            {
                Code = set?.code ?? "?",
                Name = set?.name ?? "?"
            };
            var ids = set?.priorityPasscodes ?? Array.Empty<int>();
            cov.PriorityIds = ids.Length;
            var sb = new StringBuilder();
            sb.AppendLine($"═══ SET {cov.Code} — {cov.Name} ═══");

            foreach (var id in ids)
            {
                var def = db.Get(id);
                if (def == null)
                {
                    cov.MissingFromDb++;
                    sb.AppendLine($"  MISS  {id}");
                    continue;
                }

                cov.InDatabase++;
                if (OfficialCardAuthority.HasNoActivatableEffect(def))
                {
                    cov.NormalNoEffect++;
                    cov.FullyCompiled++;
                    var tag = OfficialCardAuthority.IsNormalMonsterNoEffect(def)
                        ? "normal"
                        : "structural (effectless Extra/Fusion)";
                    sb.AppendLine($"  OK    {id} «{def.name}» {tag}");
                    continue;
                }

                var prog = CardTextEffectCompiler.Compile(def);
                var reg = OfficialEffectRegistry.HasActivatableScript(id) ||
                          MonsterEffects.IsRegisteredMonsterEffect(id);
                if (reg) cov.RegistryScripted++;

                if (prog.FullyCompiled)
                {
                    cov.FullyCompiled++;
                    sb.AppendLine($"  FULL  {id} «{def.name}» src={prog.CompileSource}");
                }
                else if (prog.ClauseList.Count > 0 || reg)
                {
                    cov.PartialCompiled++;
                    sb.AppendLine(
                        $"  PART  {id} «{def.name}» clauses={prog.ClauseList.Count} reg={reg}");
                }
                else
                {
                    sb.AppendLine($"  GAP   {id} «{def.name}» no compile, no registry");
                }
            }

            var denom = Math.Max(1, cov.InDatabase);
            var pct = 100f * cov.FullyCompiled / denom;
            sb.AppendLine(
                $"── inDB={cov.InDatabase}/{cov.PriorityIds} full={cov.FullyCompiled} " +
                $"part={cov.PartialCompiled} reg={cov.RegistryScripted} miss={cov.MissingFromDb} " +
                $"full%={pct:0.0} ──");
            cov.Report = sb.ToString();
            return cov;
        }

        public static string MeasureAllReport()
        {
            var file = Load();
            var sb = new StringBuilder();
            sb.AppendLine("PRE-LINK CURRICULUM COVERAGE");
            sb.AppendLine(file?.preLinkWall ?? "");
            if (file?.sets == null) return sb.ToString();
            foreach (var s in file.sets.OrderBy(x => x.order))
                sb.AppendLine(Measure(s).Report);
            return sb.ToString();
        }
    }
}
