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
    /// Compile path: regex templates → optional schema-validated SpaceXAI (explicit tooling opt-in only).
    /// Live duel resolution never calls the LLM — only cached programs are executed.
    /// Memory key is (cardId, eraId); the default text (no era band) is keyed "".
    /// </summary>
    public static class CompiledEffectCache
    {
        static readonly Dictionary<(int cardId, string eraId), CompiledCardProgram> Memory = new();
        static bool _diskLoaded;
        /// <summary>Programs learned since the last write; written by <see cref="Flush"/> (PERF-07).</summary>
        static bool _dirty;
        static bool _flushHooksInstalled;
        const string FileName = "wrldz_compiled_effects_v1.json";
        const string SeedFileName = "compiled_effects_seed_v1.json";

        static string DiskPath =>
            Path.Combine(Application.persistentDataPath, "WRLDZ", FileName);

        /// <summary>Shipped seed, StreamingAssets-relative: read through <see cref="StreamingFiles"/> (APK-safe).</summary>
        public const string SeedRelative = "WRLDZ/" + SeedFileName;

        /// <summary>
        /// Seed file on disk — the WRITE target for <see cref="ExportSeed"/> (Editor / tooling only;
        /// on Android this is a jar: URL and is never written). Reads use <see cref="SeedRelative"/>.
        /// </summary>
        public static string SeedPath =>
            Path.Combine(Application.streamingAssetsPath, "WRLDZ", SeedFileName);

        /// <summary>
        /// Get cached program or compile from official text (first play / first query).
        /// Order: memory/disk/seed → regex → optional AI only when explicitly enabled by tooling.
        /// </summary>
        public static CompiledCardProgram GetOrCompile(CardDef def, bool allowAi = false) =>
            GetOrCompileInternal(def, force: false, allowAi: allowAi);

        public static CompiledCardProgram GetOrCompile(CardInstance card, bool allowAi = false) =>
            GetOrCompile(card?.Def, allowAi);

        /// <summary>Cache-only lookup; never compiles or invokes AI.</summary>
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

        /// <summary>Force recompile (regex + optional explicitly enabled AI), ignoring cache for this id.</summary>
        public static CompiledCardProgram ForceRecompile(CardDef def, bool allowAi = false) =>
            GetOrCompileInternal(def, force: true, allowAi: allowAi);

        static CompiledCardProgram GetOrCompileInternal(CardDef def, bool force, bool allowAi)
        {
            if (def == null) return null;
            EnsureDiskLoaded();

            var eraId = ResolveEraId();
            var hash = string.IsNullOrEmpty(eraId)
                ? OfficialCardAuthority.TextHash(def)
                : OfficialCardAuthority.TextHash(def, eraId);
            var key = (def.id, NormalizeEra(eraId));
            if (!force &&
                Memory.TryGetValue(key, out var cached) && cached != null &&
                cached.TextHash == hash &&
                cached.CompilerVersion == CardTextEffectCompiler.Version)
            {
                // Soft residual (PositionLocks re-QC): a same-Version incomplete disk/seed entry must
                // not block a Soft land that now FullyCompiles (Curse of Fiend / Aqua Spirit / Garuda).
                // PreferProgram already picks FullyCompiled over Stub; apply it on the live compile.
                // TargetedClassics Soft cleared classify via live path after Version bump; Soft here
                // keeps Version (seed still v152) and upgrades incomplete same-Version cache in place.
                if (cached.FullyCompiled)
                    return cached;
                var liveUpgrade = CardTextEffectCompiler.Compile(def, eraId);
                if (liveUpgrade != null)
                {
                    if (string.IsNullOrEmpty(liveUpgrade.CompileSource))
                        liveUpgrade.CompileSource = "regex";
                    liveUpgrade.EraId = eraId;
                    liveUpgrade.TextHash = hash;
                }
                if (PreferProgram(liveUpgrade, cached))
                {
                    Memory[key] = liveUpgrade;
                    _dirty = true;
                    EnsureFlushHooks();
                    return liveUpgrade;
                }
                return cached;
            }

            var prog = CardTextEffectCompiler.Compile(def, eraId);
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
            // No synchronous full rewrite per cache miss during a duel (PERF-07): mark dirty and let
            // Flush write once — at duel end, when the app loses focus or quits.
            _dirty = true;
            EnsureFlushHooks();
            var nUn = prog.UnparsedFragments?.Length ?? 0;
            Debug.Log(
                $"[WRLDZ TextFX] Learned {def.id} «{def.name}»: source={prog.CompileSource} " +
                $"era={eraId} clauses={prog.ClauseList.Count} full={prog.FullyCompiled} unparsed={nUn}");
            return prog;
        }

        /// <summary>Tests set this to force an ERAZ snapshot (e.g. original). Null = cards_db.</summary>
        public static string ForcedEraId;

        /// <summary>
        /// Active match era from pending duel config. Null when no match band is set
        /// (editor tests compile <c>cards_db.json</c>). DK live play sets Original.
        /// Does not call <c>AppSession.Ensure</c> (edit-mode / batch safe).
        /// </summary>
        public static string ResolveEraId()
        {
            if (!string.IsNullOrEmpty(ForcedEraId)) return ForcedEraId;
            try
            {
                var band = AppSession.Instance != null
                    ? AppSession.Instance.PendingArMatch?.ErazBandId
                    : null;
                return string.IsNullOrEmpty(band) ? null : band;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Memory key for an era. The default text (no era band: <see cref="ResolveEraId"/> returns null)
        /// is keyed "" — the same key <see cref="GetOrCompileInternal"/> uses — so seed / disk programs
        /// loaded with an empty EraId are found again instead of being recompiled and stored twice.
        /// </summary>
        /// <summary>
        /// One cache key for the default era. Headless/Editor compiles run with no era (null) while live
        /// matches resolve ErazFormat.Original, and both read the same printed text — so both map to "".
        /// Keying them apart made the seed miss in live play (it is exported with no era) and made a
        /// re-export list a card twice. A genuinely different era keeps its own key, and the TextHash +
        /// CompilerVersion check in GetOrCompileInternal still rejects a program whose era text differs.
        /// </summary>
        static string NormalizeEra(string eraId) =>
            string.IsNullOrEmpty(eraId) ||
            string.Equals(eraId, WRLDZ.Duel.Rules.ErazFormat.Original, System.StringComparison.OrdinalIgnoreCase)
                ? ""
                : eraId;

        public static void ClearMemory()
        {
            Memory.Clear();
            _diskLoaded = false;
            _dirty = false;
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
            LoadSeed();
            LoadFromPath(DiskPath, "disk");
            if (Memory.Count > 0)
                Debug.Log($"[WRLDZ TextFX] Remembered {Memory.Count} card effect programs.");
        }

        static void LoadSeed()
        {
            try
            {
                if (!StreamingFiles.TryReadAllText(SeedRelative, out var json)) return;
                LoadFromJson(json, "seed", StreamingFiles.FullPathForLog(SeedRelative));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ TextFX] seed load failed: " + ex.Message);
            }
        }

        static void LoadFromPath(string path, string label)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                LoadFromJson(File.ReadAllText(path), label, path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WRLDZ TextFX] {label} load failed: " + ex.Message);
            }
            finally
            {
                WRLDZ.Core.EditorWorkingDirectory.Pin();
            }
        }

        static void LoadFromJson(string json, string label, string path)
        {
            var wrapper = JsonUtility.FromJson<CacheWrapper>(json);
            if (wrapper?.programs == null) return;
            if (wrapper.version != CardTextEffectCompiler.Version)
            {
                Debug.LogWarning(
                    $"[WRLDZ TextFX] {label} cache version {wrapper.version} != " +
                    $"{CardTextEffectCompiler.Version}; skipping.");
                return;
            }

            // Enums are written as ints: a file written under another enum layout would decode as other actions.
            if (!SeedEnumLayout.Compatible(wrapper.enumLayout, out var layoutWhy))
            {
                Debug.LogWarning(
                    $"[WRLDZ TextFX] {label} cache enum layout differs ({layoutWhy}); skipping. Add enum members " +
                    "at the end, bump CardTextEffectCompiler.Version and re-export the seed.");
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

        /// <summary>
        /// Write programs learned since the last write (compact JSON). Cheap no-op when nothing changed.
        /// Called at duel end (<see cref="DuelEngine"/> game over), on focus loss and on quit.
        /// </summary>
        public static void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            SaveDisk(prettyPrint: false);
        }

        static void EnsureFlushHooks()
        {
            if (_flushHooksInstalled) return;
            _flushHooksInstalled = true;
            try
            {
                Application.quitting += Flush;
                Application.focusChanged += focused =>
                {
                    if (!focused) Flush();
                };
            }
            catch (Exception ex)
            {
                // Outside a player (batch tools) the hooks may be unavailable; duel end still flushes.
                Debug.LogWarning("[WRLDZ TextFX] cache flush hooks unavailable: " + ex.Message);
            }
        }

        /// <summary>Write the whole cache now (tooling / explicit saves; pretty-printed).</summary>
        public static void SaveDiskNow()
        {
            _dirty = false;
            SaveDisk(prettyPrint: true);
        }

        static void SaveDisk(bool prettyPrint)
        {
            try
            {
                var dir = Path.GetDirectoryName(DiskPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                WriteWrapper(DiskPath, prettyPrint);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ TextFX] Cache save failed: " + ex.Message);
            }
            finally
            {
                WRLDZ.Core.EditorWorkingDirectory.Pin();
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
                var written = WriteWrapper(path, prettyPrint: true, filter);
                Debug.Log($"[WRLDZ TextFX] Exported seed ({written} programs) → {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ TextFX] Seed export failed: " + ex.Message);
                return false;
            }
        }

        static int WriteWrapper(string path, bool prettyPrint, Func<CompiledCardProgram, bool> filter = null)
        {
            var list = new List<CompiledCardProgram>();
            foreach (var p in Memory.Values)
                if (p != null && (filter == null || filter(p)))
                    list.Add(p);
            var wrapper = new CacheWrapper
            {
                version = CardTextEffectCompiler.Version,
                enumLayout = SeedEnumLayout.Current,
                programs = list.ToArray()
            };
            File.WriteAllText(path, JsonUtility.ToJson(wrapper, prettyPrint));
            return list.Count;
        }

        [Serializable]
        class CacheWrapper
        {
            public int version;
            /// <summary>The enum layout <see cref="programs"/> was written under (<see cref="SeedEnumLayout"/>).</summary>
            public string[] enumLayout;
            public CompiledCardProgram[] programs;
        }
    }

    /// <summary>
    /// The enum layout a cached program's ints mean something under. JsonUtility writes enums as ints, so a member
    /// inserted mid-list instead of appended shifts every later value. On 2026-10-18 the v205 seed read Magical
    /// Labyrinth's "Special Summon "Wall Shadow"" as InflictDamageHalfTributedAtk after EffectActionKind members
    /// were inserted mid-enum without a Version bump. Each entry is "Type|count|hash" over the type's members in value
    /// order. A file is compatible when every recorded type's first <c>count</c> members still hash the same, so
    /// appending a member keeps the seed, while inserting, renaming or removing one skips it.
    /// </summary>
    public static class SeedEnumLayout
    {
        static Dictionary<string, Type> _types;
        static string[] _current;

        /// <summary>Every enum a <see cref="CompiledCardProgram"/> serializes, by full name.</summary>
        static Dictionary<string, Type> Types
        {
            get
            {
                if (_types != null) return _types;
                var found = new Dictionary<string, Type>(StringComparer.Ordinal);
                Collect(typeof(CompiledCardProgram), found, new HashSet<Type>());
                return _types = found;
            }
        }

        public static string[] Current
        {
            get
            {
                if (_current != null) return _current;
                var names = new List<string>(Types.Keys);
                names.Sort(StringComparer.Ordinal);
                var list = new List<string>();
                foreach (var name in names)
                {
                    var members = Members(Types[name]);
                    list.Add($"{name}|{members.Count}|{Hash(members, members.Count)}");
                }

                return _current = list.ToArray();
            }
        }

        public static bool Compatible(string[] recorded, out string why)
        {
            why = null;
            if (recorded == null || recorded.Length == 0)
            {
                why = "no enum layout recorded";
                return false;
            }

            foreach (var entry in recorded)
            {
                var parts = (entry ?? "").Split('|');
                if (parts.Length != 3 || !int.TryParse(parts[1], out var count))
                {
                    why = $"bad entry '{entry}'";
                    return false;
                }

                if (!Types.TryGetValue(parts[0], out var type))
                {
                    why = $"{parts[0]} is no longer serialized";
                    return false;
                }

                var members = Members(type);
                if (members.Count < count || Hash(members, count) != parts[2])
                {
                    why = $"{type.Name} changed within its first {count} members";
                    return false;
                }
            }

            return true;
        }

        static void Collect(Type type, Dictionary<string, Type> found, HashSet<Type> seen)
        {
            if (type == null || !seen.Add(type)) return;
            foreach (var f in type.GetFields(System.Reflection.BindingFlags.Instance |
                                             System.Reflection.BindingFlags.Public))
            {
                if (f.IsNotSerialized || f.IsInitOnly) continue;
                var ft = f.FieldType;
                if (ft.IsArray) ft = ft.GetElementType();
                else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                    ft = ft.GetGenericArguments()[0];
                if (ft == null) continue;
                if (ft.IsEnum)
                    found[ft.FullName ?? ft.Name] = ft;
                else if (!ft.IsPrimitive && ft != typeof(string) && ft.IsSerializable &&
                         (ft.Namespace ?? "").StartsWith("WRLDZ", StringComparison.Ordinal))
                    Collect(ft, found, seen);
            }
        }

        /// <summary>"Name=value" in value order (Enum.GetValues / GetNames are parallel, value-sorted).</summary>
        static List<string> Members(Type enumType)
        {
            var names = Enum.GetNames(enumType);
            var values = Enum.GetValues(enumType);
            var list = new List<string>(names.Length);
            for (var i = 0; i < names.Length; i++)
                list.Add(names[i] + "=" + Convert.ToInt64(values.GetValue(i)));
            return list;
        }

        /// <summary>FNV-1a 64 over the first <paramref name="count"/> members (string.GetHashCode is per-process).</summary>
        static string Hash(List<string> members, int count)
        {
            var h = 14695981039346656037UL;
            for (var i = 0; i < count && i < members.Count; i++)
            {
                foreach (var ch in members[i] + ";")
                {
                    h ^= ch;
                    h *= 1099511628211UL;
                }
            }

            return h.ToString("x16");
        }
    }
}
