using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Remembers compiled effect programs after first play / bulk compile.
    /// Load order: StreamingAssets seed → persistent disk → memory.
    /// Compile path: regex templates → optional schema-validated SpaceXAI (first-play only).
    /// Live duel resolution never calls the LLM — only cached programs are executed.
    /// Memory key is (cardId, eraId); default era is <see cref="ErazFormat.Original"/>.
    /// </summary>
    public static class CompiledEffectCache
    {
        static readonly Dictionary<(int cardId, string eraId), CompiledCardProgram> Memory = new();
        static bool _diskLoaded;
        const string FileName = "wrldz_compiled_effects_v1.json";
        const string SeedFileName = "compiled_effects_seed_v1.json";

        static string DiskPath =>
            Path.Combine(Application.persistentDataPath, "WRLDZ", FileName);

        /// <summary>Shipped seed under StreamingAssets (offline bulk / lab).</summary>
        public static string SeedPath =>
            Path.Combine(Application.streamingAssetsPath, "WRLDZ", SeedFileName);

        /// <summary>
        /// Get cached program or compile from official text (first play / first query).
        /// Order: memory/disk/seed → regex → optional AI if incomplete and allowed.
        /// </summary>
        public static CompiledCardProgram GetOrCompile(CardDef def) =>
            GetOrCompileInternal(def, force: false, allowAi: true);

        public static CompiledCardProgram GetOrCompile(CardInstance card) =>
            GetOrCompile(card?.Def);

        public static bool TryGetCached(int cardId, out CompiledCardProgram prog) =>
            TryGetCached(cardId, ResolveEraId(), out prog);

        public static bool TryGetCached(int cardId, string eraId, out CompiledCardProgram prog)
        {
            eraId = NormalizeEra(eraId);
            return Memory.TryGetValue((cardId, eraId), out prog) && prog != null;
        }

        /// <summary>Insert or replace a validated program (Editor bulk / tooling).</summary>
        public static void Put(CompiledCardProgram prog, bool save = true)
        {
            if (prog == null || prog.CardId <= 0) return;
            EnsureDiskLoaded();
            prog.EraId = NormalizeEra(prog.EraId);
            Memory[(prog.CardId, prog.EraId)] = prog;
            if (save) SaveDiskNow();
        }

        /// <summary>Force recompile (regex + optional AI), ignoring cache for this id.</summary>
        public static CompiledCardProgram ForceRecompile(CardDef def, bool allowAi = true) =>
            GetOrCompileInternal(def, force: true, allowAi: allowAi);

        static CompiledCardProgram GetOrCompileInternal(CardDef def, bool force, bool allowAi)
        {
            if (def == null) return null;
            EnsureDiskLoaded();

            var eraId = ResolveEraId();
            var hash = OfficialCardAuthority.TextHash(def, eraId);
            var key = (def.id, eraId);
            if (!force &&
                Memory.TryGetValue(key, out var cached) && cached != null &&
                cached.TextHash == hash &&
                cached.CompilerVersion == CardTextEffectCompiler.Version)
                return cached;

            var prog = CardTextEffectCompiler.Compile(def);
            if (prog != null)
            {
                if (string.IsNullOrEmpty(prog.CompileSource))
                    prog.CompileSource = "regex";
                prog.EraId = eraId;
                prog.TextHash = hash;
            }

            // force: Editor/tooling may pass allowAi even if AllowRuntimeAi is off
            if (allowAi && ShouldTryAi(prog, def, respectRuntimeFlag: !force))
            {
                if (AiEffectCompiler.TryCompile(def, out var aiProg, out var err) && aiProg != null)
                {
                    var v = EffectProgramValidator.Validate(aiProg, def);
                    if (v.Ok && PreferProgram(aiProg, prog))
                    {
                        prog = aiProg;
                        prog.EraId = eraId;
                        prog.TextHash = hash;
                        Debug.Log(
                            $"[WRLDZ TextFX] AI compiled {def.id} «{def.name}»: " +
                            $"clauses={prog.ClauseList.Count} full={prog.FullyCompiled}");
                    }
                    else if (!v.Ok)
                    {
                        Debug.LogWarning(
                            $"[WRLDZ TextFX] AI program rejected for {def.id} «{def.name}»: {v.Error}");
                    }
                }
                else if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogWarning(
                        $"[WRLDZ TextFX] AI compile skipped/failed for {def.id} «{def.name}»: {err}");
                }
            }

            Memory[key] = prog;
            SaveDiskNow();
            var nUn = prog.UnparsedFragments?.Length ?? 0;
            Debug.Log(
                $"[WRLDZ TextFX] Learned {def.id} «{def.name}»: source={prog.CompileSource} " +
                $"era={eraId} clauses={prog.ClauseList.Count} full={prog.FullyCompiled} unparsed={nUn}");
            return prog;
        }

        /// <summary>
        /// Active match era from pending duel config; <see cref="ErazFormat.Original"/> when null.
        /// Does not call <c>AppSession.Ensure</c> (edit-mode / batch safe).
        /// </summary>
        public static string ResolveEraId()
        {
            try
            {
                var band = AppSession.Instance != null
                    ? AppSession.Instance.PendingArMatch?.ErazBandId
                    : null;
                return NormalizeEra(band);
            }
            catch
            {
                return ErazFormat.Original;
            }
        }

        static string NormalizeEra(string eraId) =>
            string.IsNullOrEmpty(eraId) ? ErazFormat.Original : eraId;

        public static void ClearMemory()
        {
            Memory.Clear();
            _diskLoaded = false;
        }

        public static int CachedCount
        {
            get
            {
                EnsureDiskLoaded();
                return Memory.Count;
            }
        }

        public static IEnumerable<CompiledCardProgram> SnapshotPrograms()
        {
            EnsureDiskLoaded();
            return new List<CompiledCardProgram>(Memory.Values);
        }

        static bool ShouldTryAi(CompiledCardProgram regexProg, CardDef def, bool respectRuntimeFlag)
        {
            if (respectRuntimeFlag && !AiEffectCompileSettings.AllowRuntimeAi) return false;
            if (!AiEffectCompiler.IsAvailable) return false;
            if (OfficialCardAuthority.HasNoActivatableEffect(def)) return false;
            // Incomplete or empty effect text → try AI (caller is already cache-miss / force).
            if (regexProg != null && regexProg.FullyCompiled) return false;
            return true;
        }

        /// <summary>Prefer fully compiled, then more clauses.</summary>
        public static bool PreferProgram(CompiledCardProgram candidate, CompiledCardProgram current)
        {
            if (candidate == null) return false;
            if (current == null) return true;
            if (candidate.FullyCompiled && !current.FullyCompiled) return true;
            if (!candidate.FullyCompiled && current.FullyCompiled) return false;
            return candidate.ClauseList.Count >= current.ClauseList.Count;
        }

        static void EnsureDiskLoaded()
        {
            if (_diskLoaded) return;
            _diskLoaded = true;
            // Seed first (shipped), then persistent overwrites with learned programs.
            LoadFromPath(SeedPath, "seed");
            LoadFromPath(DiskPath, "disk");
            if (Memory.Count > 0)
                Debug.Log($"[WRLDZ TextFX] Remembered {Memory.Count} card effect programs.");
        }

        static void LoadFromPath(string path, string label)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var wrapper = JsonUtility.FromJson<CacheWrapper>(json);
                if (wrapper?.programs == null) return;
                if (wrapper.version != CardTextEffectCompiler.Version)
                {
                    Debug.LogWarning(
                        $"[WRLDZ TextFX] {label} cache version {wrapper.version} != " +
                        $"{CardTextEffectCompiler.Version}; skipping.");
                    return;
                }

                var n = 0;
                foreach (var p in wrapper.programs)
                {
                    if (p == null || p.CardId <= 0) continue;
                    if (p.CompilerVersion != CardTextEffectCompiler.Version) continue;
                    if (string.IsNullOrEmpty(p.CompileSource))
                        p.CompileSource = label == "seed" ? "seed" : "regex";
                    p.EraId = NormalizeEra(p.EraId);
                    // Rebuild non-serialized lists from arrays
                    if (p.Clauses != null)
                        p.SetClauses(new List<EffectClause>(p.Clauses));
                    Memory[(p.CardId, p.EraId)] = p;
                    n++;
                }

                if (n > 0)
                    Debug.Log($"[WRLDZ TextFX] Loaded {n} programs from {label}: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WRLDZ TextFX] {label} load failed: " + ex.Message);
            }
        }

        public static void SaveDiskNow()
        {
            try
            {
                var dir = Path.GetDirectoryName(DiskPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                WriteWrapper(DiskPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ TextFX] Cache save failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Export current memory to StreamingAssets seed (Editor / tooling). An optional
        /// <paramref name="filter"/> limits what is written — e.g. only programs that carry
        /// compiled clauses, so the persisted "memory" excludes 0-clause vanilla / gap cards
        /// that recompile trivially anyway.
        /// </summary>
        public static bool ExportSeed(string path = null, Func<CompiledCardProgram, bool> filter = null)
        {
            try
            {
                path ??= SeedPath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                EnsureDiskLoaded();
                var written = WriteWrapper(path, filter);
                Debug.Log($"[WRLDZ TextFX] Exported seed ({written} programs) → {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ TextFX] Seed export failed: " + ex.Message);
                return false;
            }
        }

        static int WriteWrapper(string path, Func<CompiledCardProgram, bool> filter = null)
        {
            var list = new List<CompiledCardProgram>();
            foreach (var p in Memory.Values)
                if (p != null && (filter == null || filter(p)))
                    list.Add(p);
            var wrapper = new CacheWrapper
            {
                version = CardTextEffectCompiler.Version,
                programs = list.ToArray()
            };
            File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
            return list.Count;
        }

        [Serializable]
        class CacheWrapper
        {
            public int version;
            public CompiledCardProgram[] programs;
        }
    }
}
