using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Offline / Editor bulk compile of official card text → schema programs.
    /// Uses regex always; optional SpaceXAI when XAI_API_KEY is available.
    /// Live duels never call the model — export a seed for devices.
    /// </summary>
    public static class TextEffectBulkCompileMenu
    {
        static readonly string[] LabDeckFiles = EffectCoverageService.StarterAndLabDeckFiles;

        [MenuItem("WRLDZ/Text Effects/Regex Compile Lab Decks + Export Seed")]
        public static void RegexCompileLabAndExport()
        {
            // Prefer Phase 1 service (starter + lab + report + seed)
            EffectCoverageMenu.RunPhase1Menu();
        }

        [MenuItem("WRLDZ/Text Effects/AI Bulk Compile Lab Decks + Export Seed")]
        public static void AiCompileLabAndExport()
        {
            if (!EnsureAiKeyOrCancel()) return;
            RunBulk(labOnly: true, useAi: true, exportSeed: true);
        }

        [MenuItem("WRLDZ/Text Effects/Regex Compile All Cards (no AI)")]
        public static void RegexCompileAll()
        {
            if (!EditorUtility.DisplayDialog(
                    "Regex compile all",
                    "Compile every card in cards_db.json with the regex compiler only.\n" +
                    "This can take a few seconds and rewrites the persistent cache.",
                    "Compile", "Cancel"))
                return;
            RunBulk(labOnly: false, useAi: false, exportSeed: false);
        }

        [MenuItem("WRLDZ/Text Effects/AI Bulk Compile Incomplete Only (careful)")]
        public static void AiCompileIncomplete()
        {
            if (!EnsureAiKeyOrCancel()) return;
            if (!EditorUtility.DisplayDialog(
                    "AI bulk incomplete",
                    "Call SpaceXAI for every card that is not FullyCompiled after regex.\n" +
                    "This may take a long time and use API quota.\nContinue?",
                    "Compile incomplete", "Cancel"))
                return;
            RunBulk(labOnly: false, useAi: true, exportSeed: true, incompleteOnly: true);
        }

        [MenuItem("WRLDZ/Text Effects/Export Current Cache → StreamingAssets Seed")]
        public static void ExportSeedOnly()
        {
            CompiledEffectCache.ClearMemory();
            // Touch load so seed+disk merge into memory, then export
            _ = CompiledEffectCache.CachedCount;
            if (CompiledEffectCache.ExportSeed())
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "Seed exported",
                    $"Wrote {CompiledEffectCache.CachedCount} programs to:\n{CompiledEffectCache.SeedPath}",
                    "OK");
            }
            else
                EditorUtility.DisplayDialog("Seed export failed", "See Console.", "OK");
        }

        [MenuItem("WRLDZ/Text Effects/Clear Memory Cache")]
        public static void ClearCache()
        {
            CompiledEffectCache.ClearMemory();
            Debug.Log("[WRLDZ TextFX] Memory cache cleared (disk file left intact).");
        }

        [MenuItem("WRLDZ/Text Effects/Show AI Key Status")]
        public static void ShowAiStatus()
        {
            var has = AiEffectCompileSettings.HasApiKey;
            var model = AiEffectCompileSettings.Model;
            var runtime = AiEffectCompileSettings.AllowRuntimeAi;
            EditorUtility.DisplayDialog(
                "SpaceXAI effect compiler",
                $"API key: {(has ? "found (env / prefs / ai_config.json)" : "MISSING")}\n" +
                $"Model: {model}\n" +
                $"AllowRuntimeAi (first-play): {runtime}\n" +
                $"Base: {AiEffectCompileSettings.BaseUrl}\n\n" +
                "Set env XAI_API_KEY, or PlayerPrefs, or\n" +
                "persistentDataPath/WRLDZ/ai_config.json {\"apiKey\":\"...\"}.\n" +
                "Never commit real keys.",
                "OK");
        }

        static bool EnsureAiKeyOrCancel()
        {
            if (AiEffectCompileSettings.HasApiKey) return true;
            EditorUtility.DisplayDialog(
                "No XAI_API_KEY",
                "Set environment variable XAI_API_KEY (restart Unity after),\n" +
                "or PlayerPrefs wrldz_xai_api_key,\n" +
                "or persistentDataPath/WRLDZ/ai_config.json.\n\n" +
                "Never commit keys to the repo.",
                "OK");
            return false;
        }

        static void RunBulk(bool labOnly, bool useAi, bool exportSeed, bool incompleteOnly = false)
        {
            var db = CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                EditorUtility.DisplayDialog("Bulk compile", "cards_db.json failed to load.", "OK");
                return;
            }

            List<CardDef> targets;
            if (labOnly)
            {
                var ids = CollectDeckIds(LabDeckFiles);
                targets = ids
                    .Select(id => db.Get(id))
                    .Where(d => d != null)
                    .ToList();
            }
            else
            {
                targets = db.GetAllCards();
            }

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Bulk compile", "No target cards.", "OK");
                return;
            }

            CompiledEffectCache.ClearMemory();
            // Reload seed/disk so we don't discard good programs unless force-path
            _ = CompiledEffectCache.CachedCount;

            var ok = 0;
            var full = 0;
            var aiOk = 0;
            var failed = 0;
            var skipped = 0;
            var total = targets.Count;

            try
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    var def = targets[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            useAi ? "AI + regex bulk compile" : "Regex bulk compile",
                            $"{def.id} {def.name}",
                            (float)i / total))
                    {
                        Debug.LogWarning("[WRLDZ TextFX] Bulk compile cancelled by user.");
                        break;
                    }

                    if (OfficialCardAuthority.HasNoActivatableEffect(def))
                    {
                        var nm = CardTextEffectCompiler.Compile(def);
                        CompiledEffectCache.Put(nm, save: false);
                        ok++;
                        full++;
                        continue;
                    }

                    if (incompleteOnly &&
                        CompiledEffectCache.TryGetCached(def.id, out var existing) &&
                        existing.FullyCompiled &&
                        existing.TextHash == OfficialCardAuthority.TextHash(def))
                    {
                        skipped++;
                        continue;
                    }

                    // Always start from regex; AI only when requested and incomplete.
                    var prog = CardTextEffectCompiler.Compile(def);
                    if (useAi && !prog.FullyCompiled && AiEffectCompiler.IsAvailable)
                    {
                        if (AiEffectCompiler.TryCompile(def, out var aiProg, out var err) &&
                            aiProg != null)
                        {
                            var v = EffectProgramValidator.Validate(aiProg, def);
                            if (v.Ok && CompiledEffectCache.PreferProgram(aiProg, prog))
                            {
                                prog = aiProg;
                                aiOk++;
                            }
                            else if (!v.Ok)
                            {
                                Debug.LogWarning(
                                    $"[WRLDZ TextFX] AI rejected {def.id} «{def.name}»: {v.Error}");
                            }
                        }
                        else if (!string.IsNullOrEmpty(err))
                        {
                            Debug.LogWarning(
                                $"[WRLDZ TextFX] AI failed {def.id} «{def.name}»: {err}");
                            failed++;
                        }
                    }

                    CompiledEffectCache.Put(prog, save: false);
                    ok++;
                    if (prog.FullyCompiled) full++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                CompiledEffectCache.SaveDiskNow();
            }

            var seedNote = "";
            if (exportSeed)
            {
                if (CompiledEffectCache.ExportSeed())
                {
                    AssetDatabase.Refresh();
                    seedNote = $"\nSeed: {CompiledEffectCache.SeedPath}";
                }
                else
                    seedNote = "\nSeed export FAILED (see Console).";
            }

            var report =
                $"Targets: {total}\nStored: {ok}\nFullyCompiled: {full}\n" +
                $"AI accepted: {aiOk}\nAI/HTTP failures: {failed}\nSkipped (already full): {skipped}\n" +
                $"Cache count: {CompiledEffectCache.CachedCount}" +
                seedNote;
            Debug.Log("[WRLDZ TextFX] Bulk compile done.\n" + report);
            EditorUtility.DisplayDialog("Bulk compile done", report, "OK");
        }

        static HashSet<int> CollectDeckIds(string[] deckFiles)
        {
            var ids = new HashSet<int>();
            foreach (var file in deckFiles)
            {
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
    }
}
