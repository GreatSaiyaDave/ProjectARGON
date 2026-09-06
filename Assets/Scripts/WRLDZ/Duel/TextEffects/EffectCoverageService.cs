using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Phase 1 training: bulk-compile official text for deck pools + coverage report.
    /// Live duels never call the LLM — this only fills <see cref="CompiledEffectCache"/> / seed.
    /// </summary>
    public static class EffectCoverageService
    {
        /// <summary>
        /// Minimum playable coverage % on starter+lab before AI mastery self-play is "OK".
        /// Effectless Fusions count as covered (structural).
        /// </summary>
        public const float MinPlayablePctForMastery = 100f;

        /// <summary>All starter + lab decks used for Instant Duel / Desktop Lab / mastery.</summary>
        public static readonly string[] StarterAndLabDeckFiles =
        {
            "lab_rules_player.json",
            "lab_rules_ai.json",
            "player_starter.json",
            "starter_junkuriboh.json",
            "starter_kuribandit.json",
            "starter_galactikuriboh.json",
            "ai_kaiba.json"
        };

        [Serializable]
        public struct UnparsedFragmentSpan { public int Start; public int Length; public int End; public string Text; public bool Exact; public string CoordinateSystem; }

        [Serializable]
        public struct CardStatus
        {
            public int Id;
            public int Passcode;
            public string Name;
            public string Kind; // normal | full | partial | registry | gap | missing
            public int Clauses;
            public bool FullyCompiled;
            public bool RegistryScripted;
            public string Source;
            public string[] Unparsed;
            public string[] UnparsedFragments;
            public UnparsedFragmentSpan[] UnparsedSpans;
            public string[] ReasonCodes;
            public string LeftoverImpact;
            public bool LeftoverLikelyActivationBlocking;
            public bool LeftoverLikelyOptionalOrBoilerplate;
            public string[] CandidateSharedAtoms;
        }

        [Serializable]
        public class DiagnosticsReport
        {
            public string SchemaVersion = "argon.effect-coverage-diagnostics.v1";
            public string GeneratedUtc;
            public string PoolName;
            public int CompilerVersion;
            public string HeuristicNotes;
            public CardStatus[] NonFullyCompiled;
        }

        public struct PoolReport
        {
            public string PoolName;
            public int UniqueIds;
            public int InDatabase;
            public int MissingFromDb;
            public int NormalNoEffect;
            public int FullyCompiled;
            public int PartialCompiled;
            public int RegistryOnly;
            public int Gap;
            /// <summary>normal + full + registry (playable without inventing text).</summary>
            public int PlayableCovered;
            public float PlayablePct;
            public float FullCompilePct;
            public string Summary;
            public string Detail;
            public List<CardStatus> Cards;
            public string ReportPath;
            public string DiagnosticsPath;
            public string SeedPath;
            public bool SeedExported;
        }

        public struct BulkCompileResult
        {
            public int Targets;
            public int Stored;
            public int FullyCompiled;
            public int AiAccepted;
            public int Failed;
            public PoolReport Coverage;
            public string Log;
        }

        /// <summary>
        /// Collect unique passcodes from deck JSON files under StreamingAssets/Decks.
        /// </summary>
        public static HashSet<int> CollectDeckIds(IEnumerable<string> deckFiles)
        {
            var ids = new HashSet<int>();
            if (deckFiles == null) return ids;
            foreach (var file in deckFiles)
            {
                if (string.IsNullOrEmpty(file)) continue;
                var deck = CardDatabase.LoadDeck(file);
                if (deck == null) continue;
                AddEntries(ids, deck.main);
                AddEntries(ids, deck.extra);
                AddEntries(ids, deck.side);
            }

            return ids;
        }

        static void AddEntries(HashSet<int> ids, DeckCardEntry[] entries)
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e != null && e.id > 0)
                    ids.Add(e.id);
            }
        }

        /// <summary>
        /// Regex-compile (and optional AI) every card in the pool into the cache.
        /// Does not invent resolutions for unparseable text.
        /// </summary>
        public static BulkCompileResult BulkCompile(
            IEnumerable<int> cardIds,
            CardDatabase db = null,
            bool useAi = false,
            bool exportSeed = true,
            bool clearMemoryFirst = true)
        {
            db ??= CardDatabase.Instance ?? CardDatabase.Load();
            var result = new BulkCompileResult();
            var sb = new StringBuilder();
            sb.AppendLine("═══ EFFECT BULK COMPILE ═══");

            if (db == null || db.Count == 0)
            {
                result.Log = "cards_db failed to load.";
                return result;
            }

            var ids = cardIds?.Where(id => id > 0).Distinct().OrderBy(id => id).ToList()
                      ?? new List<int>();
            result.Targets = ids.Count;

            if (clearMemoryFirst)
            {
                CompiledEffectCache.ClearMemory();
                _ = CompiledEffectCache.CachedCount; // reload seed/disk
            }

            var full = 0;
            var stored = 0;
            var aiOk = 0;
            var failed = 0;

            foreach (var id in ids)
            {
                var def = db.Get(id);
                if (def == null)
                {
                    sb.AppendLine($"  MISS  {id}");
                    continue;
                }

                if (OfficialCardAuthority.HasNoActivatableEffect(def))
                {
                    var nm = CardTextEffectCompiler.Compile(def);
                    CompiledEffectCache.Put(nm, save: false);
                    stored++;
                    full++;
                    continue;
                }

                var prog = CardTextEffectCompiler.Compile(def);
                if (useAi && !prog.FullyCompiled && AiEffectCompiler.IsAvailable)
                {
                    if (AiEffectCompiler.TryCompile(def, out var aiProg, out var err) && aiProg != null)
                    {
                        var v = EffectProgramValidator.Validate(aiProg, def);
                        if (v.Ok && CompiledEffectCache.PreferProgram(aiProg, prog))
                        {
                            prog = aiProg;
                            aiOk++;
                        }
                        else if (!v.Ok)
                        {
                            sb.AppendLine($"  AI-REJ {id} «{def.name}»: {v.Error}");
                            failed++;
                        }
                    }
                    else if (!string.IsNullOrEmpty(err))
                    {
                        sb.AppendLine($"  AI-FAIL {id} «{def.name}»: {err}");
                        failed++;
                    }
                }

                CompiledEffectCache.Put(prog, save: false);
                stored++;
                if (prog.FullyCompiled) full++;
                else if (prog.ClauseList.Count == 0 &&
                         !OfficialEffectRegistry.HasActivatableScript(id) &&
                         !MonsterEffects.IsRegisteredMonsterEffect(id))
                    sb.AppendLine($"  GAP   {id} «{def.name}»");
            }

            CompiledEffectCache.SaveDiskNow();
            result.Stored = stored;
            result.FullyCompiled = full;
            result.AiAccepted = aiOk;
            result.Failed = failed;

            var seedOk = false;
            if (exportSeed)
                seedOk = CompiledEffectCache.ExportSeed();

            sb.AppendLine(
                $"Stored={stored}/{ids.Count} full={full} ai={aiOk} fail={failed} " +
                $"cache={CompiledEffectCache.CachedCount} seed={seedOk}");

            result.Coverage = MeasurePool("bulk_targets", ids, db, writeReportFile: true);
            result.Coverage.SeedExported = seedOk;
            result.Coverage.SeedPath = CompiledEffectCache.SeedPath;
            result.Log = sb.ToString() + "\n" + result.Coverage.Summary;
            Debug.Log("[WRLDZ TextFX] " + result.Log);
            return result;
        }

        /// <summary>Bulk compile starter + lab decks (Phase 1 default).</summary>
        public static BulkCompileResult BulkCompileStarterAndLab(bool useAi = false, bool exportSeed = true) =>
            BulkCompile(CollectDeckIds(StarterAndLabDeckFiles), useAi: useAi, exportSeed: exportSeed);

        /// <summary>
        /// Measure coverage for a pool of passcodes without necessarily recompiling.
        /// Uses cache when present; otherwise regex-compiles on the fly (does not save unless Put).
        /// </summary>
        public static PoolReport MeasurePool(
            string poolName,
            IEnumerable<int> cardIds,
            CardDatabase db = null,
            bool writeReportFile = true)
        {
            db ??= CardDatabase.Instance ?? CardDatabase.Load();
            var report = new PoolReport
            {
                PoolName = poolName ?? "pool",
                Cards = new List<CardStatus>()
            };
            var ids = cardIds?.Where(id => id > 0).Distinct().OrderBy(id => id).ToList()
                      ?? new List<int>();
            report.UniqueIds = ids.Count;

            var detail = new StringBuilder();
            detail.AppendLine($"═══ EFFECT COVERAGE — {report.PoolName} ═══");
            detail.AppendLine($"Generated: {DateTime.UtcNow:o} UTC");
            detail.AppendLine($"Compiler v{CardTextEffectCompiler.Version}");
            detail.AppendLine();

            foreach (var id in ids)
            {
                var st = Classify(id, db);
                report.Cards.Add(st);
                switch (st.Kind)
                {
                    case "missing":
                        report.MissingFromDb++;
                        detail.AppendLine($"  MISS     {id}");
                        break;
                    case "normal":
                    case "structural":
                        report.InDatabase++;
                        report.NormalNoEffect++;
                        report.FullyCompiled++;
                        report.PlayableCovered++;
                        detail.AppendLine(
                            st.Kind == "structural"
                                ? $"  STRUCT   {id} «{st.Name}» (effectless Extra/Fusion)"
                                : $"  NORMAL   {id} «{st.Name}»");
                        break;
                    case "full":
                        report.InDatabase++;
                        report.FullyCompiled++;
                        report.PlayableCovered++;
                        detail.AppendLine(
                            $"  FULL     {id} «{st.Name}» clauses={st.Clauses} src={st.Source}");
                        break;
                    case "registry":
                        report.InDatabase++;
                        report.RegistryOnly++;
                        report.PlayableCovered++;
                        detail.AppendLine(
                            $"  REGISTRY {id} «{st.Name}» (hard-coded script; text partial/gap)");
                        break;
                    case "partial":
                        report.InDatabase++;
                        report.PartialCompiled++;
                        detail.AppendLine(
                            $"  PARTIAL  {id} «{st.Name}» clauses={st.Clauses} src={st.Source}");
                        if (st.Unparsed != null && st.Unparsed.Length > 0)
                            detail.AppendLine($"           unparsed: {string.Join(" | ", st.Unparsed.Take(2))}");
                        break;
                    default:
                        report.InDatabase++;
                        report.Gap++;
                        detail.AppendLine($"  GAP      {id} «{st.Name}» — no compile, no registry");
                        break;
                }
            }

            var denom = Math.Max(1, report.InDatabase);
            report.PlayablePct = 100f * report.PlayableCovered / denom;
            report.FullCompilePct = 100f * report.FullyCompiled / denom;

            var summary = new StringBuilder();
            summary.AppendLine($"Pool «{report.PoolName}» unique={report.UniqueIds}");
            summary.AppendLine(
                $"  inDB={report.InDatabase} miss={report.MissingFromDb} " +
                $"normal={report.NormalNoEffect} full={report.FullyCompiled - report.NormalNoEffect} " +
                $"partial={report.PartialCompiled} registry={report.RegistryOnly} gap={report.Gap}");
            summary.AppendLine(
                $"  playableCovered={report.PlayableCovered}/{report.InDatabase} " +
                $"({report.PlayablePct:0.0}%)  fullCompile%={report.FullCompilePct:0.0}%");
            summary.AppendLine(
                "  playable = normal monsters + FullyCompiled text + hard-coded registry scripts");

            // Top gaps for prioritization
            var gaps = report.Cards.Where(c => c.Kind == "gap" || c.Kind == "partial").Take(40).ToList();
            if (gaps.Count > 0)
            {
                summary.AppendLine("  Priority gaps (first 40):");
                foreach (var g in gaps)
                    summary.AppendLine($"    · {g.Id} «{g.Name}» [{g.Kind}]");
            }

            report.Summary = summary.ToString();
            report.Detail = detail.ToString() + "\n" + report.Summary;

            if (writeReportFile)
            {
                try
                {
                    var dir = Path.Combine(Application.persistentDataPath, "WRLDZ", "reports");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var safe = SafeFilePart(poolName);
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    var path = Path.Combine(dir, $"effect_coverage_{safe}_{stamp}.txt");
                    File.WriteAllText(path, report.Detail);
                    report.ReportPath = path;
                    // Also write a stable latest path for tooling
                    var latest = Path.Combine(dir, $"effect_coverage_{safe}_latest.txt");
                    File.WriteAllText(latest, report.Detail);
                    report.DiagnosticsPath = WriteDiagnosticsJson(report, dir, safe, stamp);
                    Debug.Log("[WRLDZ TextFX] Coverage report → " + path);
                    if (!string.IsNullOrEmpty(report.DiagnosticsPath))
                        Debug.Log("[WRLDZ TextFX] Diagnostics JSON → " + report.DiagnosticsPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ TextFX] Report write failed: " + ex.Message);
                }
            }

            return report;
        }

        public static bool DiagnosticsSmoke(CardDatabase db, out string error)
        {
            error = string.Empty;
            try
            {
                db ??= CardDatabase.Instance ?? CardDatabase.Load();
                if (db == null) { error = "cards_db failed to load"; return false; }
                var ids = db.GetAllCards()
                    .Where(c => c != null && c.id > 0 && !OfficialCardAuthority.HasNoActivatableEffect(c))
                    .Select(c => c.id).Take(8).ToList();
                var report = MeasurePool("diagnostics_smoke", ids, db, writeReportFile: false);
                foreach (var card in report.Cards)
                {
                    if (card.FullyCompiled) continue;
                    if (card.ReasonCodes == null || card.UnparsedSpans == null ||
                        string.IsNullOrEmpty(card.LeftoverImpact))
                    {
                        error = $"missing diagnostics fields for {card.Id} «{card.Name}»";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        static void PopulateDiagnostics(ref CardStatus st, CardDef def, CompiledCardProgram prog)
        {
            var source = prog?.SourceText ?? OfficialCardAuthority.OfficialText(def) ?? string.Empty;
            var fragments = prog?.UnparsedFragments ?? Array.Empty<string>();
            st.UnparsedSpans = FindFragmentSpans(source, fragments);
            var reasons = new List<string>();
            var hints = new List<string>();
            var hasBlocking = false;
            var allOptionalOrBoilerplate = fragments.Length > 0;
            foreach (var fragment in fragments)
            {
                if (string.IsNullOrWhiteSpace(fragment)) continue;
                var optional = IsOptionalOrBoilerplate(fragment);
                allOptionalOrBoilerplate &= optional;
                if (optional) AddReason(reasons, "BoilerplateCandidate");
                var hasCost = Regex.IsMatch(fragment, @"\b(discard|tribute|send|pay|banish|reveal|detach|return)\b.*(?:cost|;|to the GY|from the)", RegexOptions.IgnoreCase);
                var hasTarget = Regex.IsMatch(fragment, @"\b(target(?:s|ed|ing)?|select|choose)\b", RegexOptions.IgnoreCase);
                var hasTrigger = Regex.IsMatch(fragment, @"\b(when|if|during|after|before|at the start|at the end)\b", RegexOptions.IgnoreCase);
                var hasZone = Regex.IsMatch(fragment, @"\b(from|in|on)\s+(?:your |your opponent's |the )?(hand|Deck|Graveyard|GY|field|banished zone)\b", RegexOptions.IgnoreCase);
                var hasAction = Regex.IsMatch(fragment, @"\b(destroy|special summon|summon|negate|gain|lose|inflict|banish|draw|add|return|send|change|discard|equip|target)\b", RegexOptions.IgnoreCase);
                if (hasCost) { AddReason(reasons, "UnparsedCost"); hasBlocking = true; }
                if (hasTarget) { AddReason(reasons, "UnparsedTarget"); hasBlocking = true; }
                if (hasTrigger) { AddReason(reasons, "UnknownTrigger"); hasBlocking = true; }
                if (hasZone) { AddReason(reasons, "UnknownSourceZone"); hasBlocking = true; }
                if (hasAction)
                {
                    AddReason(reasons, "UnparsedResolution");
                    if (!st.RegistryScripted) AddReason(reasons, "RuntimeActionMissing");
                    hasBlocking = true;
                }
                AddCandidateHints(hints, fragment);
            }
            var official = OfficialCardAuthority.OfficialText(def) ?? string.Empty;
            if (!string.Equals(source, official, StringComparison.Ordinal)) AddReason(reasons, "DataTextMismatch");
            var hasBoilerplate = HasBoilerplateCandidate(source);
            if (hasBoilerplate) AddReason(reasons, "BoilerplateCandidate");
            AddCandidateHints(hints, source);
            if (reasons.Count == 0) AddReason(reasons, "Other");
            st.ReasonCodes = reasons.ToArray();
            st.CandidateSharedAtoms = hints.ToArray();
            st.LeftoverLikelyActivationBlocking = hasBlocking;
            st.LeftoverLikelyOptionalOrBoilerplate = !hasBlocking && (allOptionalOrBoilerplate || hasBoilerplate);
            st.LeftoverImpact = fragments.Length == 0 ? (hasBoilerplate ? "OptionalOrBoilerplate" : "None") : hasBlocking ? "ActivationBlocking" :
                st.LeftoverLikelyOptionalOrBoilerplate ? "OptionalOrBoilerplate" : "Uncertain";
        }

        static UnparsedFragmentSpan[] FindFragmentSpans(string source, string[] fragments)
        {
            if (fragments == null || fragments.Length == 0) return Array.Empty<UnparsedFragmentSpan>();
            var normalized = NormalizeForDiagnostics(source);
            var spans = new UnparsedFragmentSpan[fragments.Length];
            var cursor = 0;
            for (var i = 0; i < fragments.Length; i++)
            {
                var text = fragments[i] ?? string.Empty;
                var start = string.IsNullOrEmpty(text) ? -1 : normalized.IndexOf(text, cursor, StringComparison.OrdinalIgnoreCase);
                if (start < 0 && !string.IsNullOrEmpty(text)) start = normalized.IndexOf(text, StringComparison.OrdinalIgnoreCase);
                var exact = start >= 0;
                spans[i] = new UnparsedFragmentSpan
                {
                    Start = exact ? start : -1, Length = exact ? text.Length : 0,
                    End = exact ? start + text.Length : -1, Text = text, Exact = exact,
                    CoordinateSystem = "normalizedOfficialText"
                };
                if (exact) cursor = start + text.Length;
            }
            return spans;
        }

        static string NormalizeForDiagnostics(string text)
        {
            return Regex.Replace((text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' '), @"\s+", " ").Trim();
        }

        static bool IsOptionalOrBoilerplate(string text)
        {
            return Regex.IsMatch(text ?? string.Empty,
                @"(?:once per turn|you can only activate 1|you can only use 1|you can use this effect only once|if you do|you can|may)",
                RegexOptions.IgnoreCase);
        }

        static bool HasBoilerplateCandidate(string text)
        {
            return Regex.IsMatch(text ?? string.Empty, @"(?:once per turn|you can only activate 1|you can only use 1|you can use this effect only once)", RegexOptions.IgnoreCase);
        }

        static void AddReason(List<string> reasons, string reason)
        {
            if (!reasons.Contains(reason)) reasons.Add(reason);
        }

        static void AddCandidateHints(List<string> hints, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            void Hint(string value) { if (!hints.Contains(value)) hints.Add(value); }
            if (Regex.IsMatch(text, @"(?:destroy|banish).{0,80}(?:highest|lowest|strongest|weakest).{0,40}(?:ATK|DEF)", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"(?:highest|lowest).{0,40}(?:ATK|DEF).{0,80}(?:destroy|banish)", RegexOptions.IgnoreCase)) Hint("DestroyExtremum");
            if (Regex.IsMatch(text, @"(?:gain|increase|lose|decrease).{0,30}(?:ATK|DEF).{0,50}(?:until|end phase|end of this turn)", RegexOptions.IgnoreCase)) Hint("GainAtkDefUntilEnd");
            if (Regex.IsMatch(text, @"special summon", RegexOptions.IgnoreCase))
            {
                if (Regex.IsMatch(text, @"from (?:the )?GY|from your Graveyard|from the Graveyard", RegexOptions.IgnoreCase)) Hint("SpecialSummonFromGy");
                if (Regex.IsMatch(text, @"from (?:your )?hand", RegexOptions.IgnoreCase)) Hint("SpecialSummonFromHand");
                if (Regex.IsMatch(text, @"from (?:your )?Deck", RegexOptions.IgnoreCase)) Hint("SpecialSummonFromDeck");
                if (Regex.IsMatch(text, @"from the Extra Deck", RegexOptions.IgnoreCase)) Hint("SpecialSummonFromExtra");
                Hint("SpecialSummonFrom*");
            }
            if (Regex.IsMatch(text, @"negate (?:the )?activation|negate (?:that )?effect", RegexOptions.IgnoreCase)) Hint("NegateActivation");
            if (Regex.IsMatch(text, @"\b(?:when|if)\b.*\b(?:you can|may)\b", RegexOptions.IgnoreCase)) Hint("WhenIfOptional");
            if (Regex.IsMatch(text, @"\b(?:select|choose)\b", RegexOptions.IgnoreCase) && Regex.IsMatch(text, @"\btarget\b", RegexOptions.IgnoreCase)) Hint("SelectVsTarget");
            if (Regex.IsMatch(text, @"change (?:its |the )?(?:battle )?position|face-down Defense Position|face-up Attack Position", RegexOptions.IgnoreCase)) Hint("ChangePosition");
        }

        static string SafeFilePart(string value)
        {
            return string.Join("_", (value ?? "pool").Split(Path.GetInvalidFileNameChars()));
        }

        static string WriteDiagnosticsJson(PoolReport report, string dir, string safe, string stamp)
        {
            try
            {
                var payload = new DiagnosticsReport
                {
                    GeneratedUtc = DateTime.UtcNow.ToString("o"), PoolName = report.PoolName,
                    CompilerVersion = CardTextEffectCompiler.Version,
                    HeuristicNotes = "Best-effort heuristics; spans use normalizedOfficialText offsets. Exact=false means no direct match. FullyCompiled semantics unchanged.",
                    NonFullyCompiled = (report.Cards ?? new List<CardStatus>()).Where(c => !c.FullyCompiled).ToArray()
                };
                var json = JsonUtility.ToJson(payload, true);
                var path = Path.Combine(dir, $"effect_diagnostics_{safe}_{stamp}.json");
                File.WriteAllText(path, json);
                File.WriteAllText(Path.Combine(dir, $"effect_diagnostics_{safe}_latest.json"), json);
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ TextFX] Diagnostics JSON write failed: " + ex.Message);
                return string.Empty;
            }
        }

        public static PoolReport MeasureStarterAndLab(CardDatabase db = null, bool writeReportFile = true) =>
            MeasurePool("starter_and_lab", CollectDeckIds(StarterAndLabDeckFiles), db, writeReportFile);

        /// <summary>Coverage of every passcode in cards_db — the playable pool, not just lab decks.</summary>
        public static PoolReport MeasureAllCardsDb(CardDatabase db = null, bool writeReportFile = true)
        {
            db ??= CardDatabase.Instance ?? CardDatabase.Load();
            var ids = db == null
                ? new List<int>()
                : db.GetAllCards().Where(c => c != null && c.id > 0).Select(c => c.id);
            return MeasurePool("cards_db", ids, db, writeReportFile);
        }

        /// <summary>
        /// Cards whose official text matches a shared kind we claim to compile.
        /// A new card added to cards_db with that shape must compile — no cardId branch.
        /// </summary>
        public static List<string> SharedKindCompileGaps(CardDatabase db = null)
        {
            db ??= CardDatabase.Instance ?? CardDatabase.Load();
            var gaps = new List<string>();
            if (db == null) return gaps;
            foreach (var def in db.GetAllCards())
            {
                if (def == null || OfficialCardAuthority.HasNoActivatableEffect(def)) continue;
                var need = new List<EffectActionKind>();
                ProtectionTemplates.ExpectedActions(def.desc, need);
                LegacyTextTemplates.ExpectedActions(def, need);
                PhaseTriggerTemplates.ExpectedActions(def, need);
                ContinuousRestrictionTemplates.ExpectedActions(def, need);
                MonsterTriggerTemplates.ExpectedActions(def, need);

                if (need.Count == 0) continue;
                var prog = CardTextEffectCompiler.Compile(def);
                foreach (var action in need)
                {
                    var hit = prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);
                    if (!hit)
                        gaps.Add($"{def.id} «{def.name}» missing {action}");
                }
            }

            return gaps;
        }

        static CardStatus Classify(int id, CardDatabase db)
        {
            var st = new CardStatus { Id = id, Name = "?", Kind = "missing" };
            var def = db?.Get(id);
            if (def == null) return st;

            st.Name = def.name ?? $"#{id}";
            st.Passcode = id;
            var reg = OfficialEffectRegistry.HasActivatableScript(id) ||
                      MonsterEffects.IsRegisteredMonsterEffect(id);
            st.RegistryScripted = reg;

            if (OfficialCardAuthority.HasNoActivatableEffect(def))
            {
                st.FullyCompiled = true;
                if (OfficialCardAuthority.IsNormalMonsterNoEffect(def))
                {
                    st.Kind = "normal";
                    st.Source = "normal";
                }
                else
                {
                    st.Kind = "structural";
                    st.Source = "structural";
                }

                return st;
            }

            CompiledCardProgram prog = null;
            if (CompiledEffectCache.TryGetCached(id, out var cached) &&
                cached != null &&
                cached.TextHash == OfficialCardAuthority.TextHash(def) &&
                cached.CompilerVersion == CardTextEffectCompiler.Version)
                prog = cached;
            else
                prog = CardTextEffectCompiler.Compile(def);

            st.Clauses = prog?.ClauseList?.Count ?? 0;
            st.FullyCompiled = prog != null && prog.FullyCompiled;
            st.Source = prog?.CompileSource ?? "";
            st.Unparsed = prog?.UnparsedFragments ?? Array.Empty<string>();
            st.UnparsedFragments = st.Unparsed;
            if (!st.FullyCompiled) PopulateDiagnostics(ref st, def, prog);

            if (st.FullyCompiled)
                st.Kind = "full";
            else if (reg)
                st.Kind = "registry"; // hard-coded script covers play even if text partial
            else if (st.Clauses > 0)
                st.Kind = "partial";
            else
                st.Kind = "gap";

            return st;
        }

        /// <summary>One-shot Phase 1: compile starters+lab, export seed, return full log.</summary>
        public static string RunPhase1(bool useAi = false)
        {
            var bulk = BulkCompileStarterAndLab(useAi: useAi, exportSeed: true);
            var era = CardEraCurriculum.MeasureAllReport();
            var all = new StringBuilder();
            all.AppendLine(bulk.Log);
            all.AppendLine();
            all.AppendLine(era);
            all.AppendLine();
            if (!string.IsNullOrEmpty(bulk.Coverage.ReportPath))
                all.AppendLine("Report file: " + bulk.Coverage.ReportPath);
            if (!string.IsNullOrEmpty(bulk.Coverage.DiagnosticsPath))
                all.AppendLine("Diagnostics JSON: " + bulk.Coverage.DiagnosticsPath);
            if (bulk.Coverage.SeedExported)
                all.AppendLine("Seed: " + CompiledEffectCache.SeedPath);
            all.AppendLine(MasteryGateLine(bulk.Coverage));
            return all.ToString();
        }

        /// <summary>
        /// True when starter+lab pool is fully playable (no effect gaps).
        /// Used to gate AI mastery self-play promotion.
        /// </summary>
        public static bool MeetsMasteryCoverageGate(out PoolReport report, out string reason)
        {
            report = MeasureStarterAndLab(db: null, writeReportFile: false);
            if (report.PlayablePct + 0.001f >= MinPlayablePctForMastery && report.Gap == 0)
            {
                reason = $"OK playable={report.PlayablePct:0.0}% gap={report.Gap}";
                return true;
            }

            reason =
                $"Coverage gate FAIL: playable={report.PlayablePct:0.0}% " +
                $"(need ≥{MinPlayablePctForMastery:0}%) gap={report.Gap} partial={report.PartialCompiled}";
            return false;
        }

        public static string MasteryGateLine(PoolReport report)
        {
            if (report.PlayablePct + 0.001f >= MinPlayablePctForMastery && report.Gap == 0)
                return $"MASTERY GATE: PASS playable={report.PlayablePct:0.0}% gap=0";
            return $"MASTERY GATE: FAIL playable={report.PlayablePct:0.0}% " +
                   $"gap={report.Gap} partial={report.PartialCompiled} " +
                   $"(need ≥{MinPlayablePctForMastery:0}% and gap=0)";
        }
    }
}
