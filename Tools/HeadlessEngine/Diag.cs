// Card inspector: prints how a card's official text compiles into an effect
// program. Useful when authoring/adding cards and for diagnosing why a card is
// classified unimplemented/stub or refuses to activate.
//
//   Tools/HeadlessEngine/run.sh --card "Call of the Haunted"
//   Tools/HeadlessEngine/run.sh --card 97077563
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Duel.TextEffects;

internal static class Diag
{
    public static int InspectCard(string query)
    {
        var db = CardDatabase.Load();
        CardDef def = null;
        if (int.TryParse(query, out var id)) db.TryGet(id, out def);
        if (def == null)
            def = db.GetAllCards().FirstOrDefault(c =>
                string.Equals(c.name, query, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            Console.Error.WriteLine($"card not found: {query}");
            return 2;
        }

        Console.WriteLine($"#{def.id}  {def.name}");
        var _stats = def.IsMonster ? $"Lv{def.level} {def.atk}/{def.def}" : "";
        var _sc = (def.scaleLeft > 0 || def.scaleRight > 0) ? $"  Sc:{def.scaleLeft}--{def.scaleRight}" : "";
        Console.WriteLine($"  type={def.type}  race={def.race}  attr={def.attribute}  {_stats}{_sc}");
        Console.WriteLine($"  desc: {def.desc}");
        Console.WriteLine();
        PrintCompiled(def);
        return 0;
    }

    // One monster from eras/prelink_monsters.json. Does not compile the rest of the pool
    // and does not write Docs/PRELINK_MONSTER_COMPILE.md.
    public static int InspectPrelinkCard(string query)
    {
        if (!TryLoadPrelink(out var file, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        query = (query ?? "").Trim();
        if (query.Length == 0)
        {
            Console.Error.WriteLine("prelink-card requires a name or passcode");
            return 2;
        }

        var matches = new List<PrelinkCardRow>();
        bool byId = int.TryParse(query, out var id);
        foreach (var row in file.cards)
        {
            if (row == null) continue;
            if (byId)
            {
                if (row.id == id) matches.Add(row);
            }
            else if (string.Equals(row.name, query, StringComparison.OrdinalIgnoreCase))
                matches.Add(row);
        }
        if (matches.Count == 0)
        {
            Console.Error.WriteLine("not in prelink pool: " + query);
            return 2;
        }
        if (matches.Count > 1)
        {
            Console.Error.WriteLine($"prelink pool has {matches.Count} rows for {query}");
            return 2;
        }

        var hit = matches[0];
        var def = hit.ToDef();
        Console.WriteLine($"#{def.id}  {def.name}");
        Console.WriteLine($"  pool: prelink wall={file.wall}");
        var _stats = def.IsMonster ? $"Lv{def.level} {def.atk}/{def.def}" : "";
        var _sc = (def.scaleLeft > 0 || def.scaleRight > 0) ? $"  Sc:{def.scaleLeft}--{def.scaleRight}" : "";
        Console.WriteLine($"  type={def.type}  race={def.race}  attr={def.attribute}  {_stats}{_sc}");
        Console.WriteLine($"  desc: {def.desc}");
        var db = CardDatabase.Load();
        if (db.TryGet(def.id, out var dbDef) && dbDef != null)
        {
            bool same = (dbDef.desc ?? "") == (def.desc ?? "");
            Console.WriteLine(same ? "  cards_db: present, same desc" : "  cards_db: present, desc differs");
        }
        else
            Console.WriteLine("  cards_db: absent");
        Console.WriteLine();
        PrintCompiled(def);
        return 0;
    }

    static void PrintCompiled(CardDef def)
    {
        var status = WRLDZ.Duel.CardEffectStatus.Classify(def);
        Console.WriteLine($"  classify: {status}");
        Console.WriteLine($"  ProgramMayActivate: {OfficialEffectRegistry.ProgramMayActivate(def)}");

        var prog = CardTextEffectCompiler.Compile(def);
        if (prog == null) { Console.WriteLine("  compile: <null program>"); return; }
        Console.WriteLine($"  FullyCompiled: {prog.FullyCompiled}");
        Console.WriteLine($"  CompileSource: {prog.CompileSource}");
        if (prog.UnparsedFragments != null && prog.UnparsedFragments.Length > 0)
            foreach (var fragment in prog.UnparsedFragments)
                Console.WriteLine($"  unparsed: {fragment}");
        Console.WriteLine($"  timings: {string.Join(", ", Enum.GetValues(typeof(EffectTiming)).Cast<EffectTiming>().Where(prog.HasTiming))}");
        Console.WriteLine($"  clauses: {prog.ClauseList.Count}");
        int i = 0;
        foreach (var c in prog.ClauseList)
        {
            i++;
            if (c == null) { Console.WriteLine($"    [{i}] <null>"); continue; }
            Console.WriteLine($"    [{i}] Timing={c.Timing} Action={c.Action} Side={c.Side} Zone={c.Zone}");
            var setFlags = typeof(EffectClause).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => IsInteresting(f, c))
                .Select(f => $"{f.Name}={Fmt(f.GetValue(c))}");
            var joined = string.Join(", ", setFlags);
            if (!string.IsNullOrEmpty(joined)) Console.WriteLine($"        {joined}");
            if (c?.SourceSnippet != null) Console.WriteLine($"        SourceSnippet={c.SourceSnippet}");
        }
    }

    static bool TryLoadPrelink(out PrelinkCatalogFile file, out string error)
    {
        file = null;
        var path = Path.Combine(UnityEngine.Application.streamingAssetsPath, "WRLDZ", "eras", "prelink_monsters.json");
        if (!File.Exists(path))
        {
            error = "FAIL  prelink: missing " + path;
            return false;
        }
        file = UnityEngine.JsonUtility.FromJson<PrelinkCatalogFile>(File.ReadAllText(path));
        if (file?.cards == null || file.cards.Length == 0)
        {
            error = "FAIL  prelink: catalog did not parse";
            return false;
        }
        if (file.wall != "2017-05-05")
        {
            error = "FAIL  prelink: wall is " + file.wall;
            return false;
        }
        error = null;
        return true;
    }

    // Reports effect-compile coverage of the pre-Link (Original ERAZ) pool and
    // the whole card DB, so progress toward "all pre-Link cards playable" is a
    // tracked number rather than a guess.
    public static int Coverage()
    {
        CardDatabase.Load();

        Console.WriteLine("== Pre-Link set coverage (CardEraCurriculum) ==");
        Console.WriteLine(CardEraCurriculum.MeasureAllReport().TrimEnd());
        Console.WriteLine();

        Console.WriteLine("== Whole cards_db coverage (EffectCoverageService) ==");
        var all = EffectCoverageService.MeasureAllCardsDb(writeReportFile: false);
        PrintPool(all);
        Console.WriteLine();

        Console.WriteLine("== Starter + lab mastery pool ==");
        var pool = EffectCoverageService.MeasureStarterAndLab(writeReportFile: false);
        PrintPool(pool);
        Console.WriteLine("  " + EffectCoverageService.MasteryGateLine(pool));
        return 0;
    }

    // Every card's engine status, one TSV row each, for before/after batch diffs
    // (Tools/CardCompile/status_diff.py) and worklists (Tools/CardCompile/worklist.py):
    //   id <TAB> status <TAB> set <TAB> type <TAB> race <TAB> name
    // status = Structural | Implemented | Stub | Unimplemented (CardEffectStatus.Classify);
    // set = the first CardEraCurriculum set (LOB, MRD, SRL …) listing the card, "-" if none.
    public static int StatusDump()
    {
        var db = CardDatabase.Load();
        var firstSet = new System.Collections.Generic.Dictionary<int, string>();
        var sets = CardEraCurriculum.Load()?.sets;
        if (sets != null)
            foreach (var s in sets.OrderBy(s => s.order))
                foreach (var pid in s.priorityPasscodes ?? Array.Empty<int>())
                    if (!firstSet.ContainsKey(pid))
                        firstSet[pid] = s.code;
        var sb = new System.Text.StringBuilder();
        var n = 0;
        foreach (var def in db.GetAllCards())
        {
            if (def == null) continue;
            var kind = WRLDZ.Duel.CardEffectStatus.Classify(def);
            var set = firstSet.TryGetValue(def.id, out var code) ? code : "-";
            sb.Append(def.id).Append('\t').Append(kind).Append('\t').Append(set).Append('\t')
                .Append(Clean(def.type)).Append('\t').Append(Clean(def.race)).Append('\t')
                .Append(Clean(def.name)).Append('\n');
            n++;
        }

        Console.Out.Write(sb.ToString());
        Console.Error.WriteLine($"# status rows: {n}");
        return 0;
    }

    static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ');

    // Dumps every card the engine classifies as Unimplemented (no compiled clauses,
    // no registry script), tab-separated "id<TAB>type<TAB>race<TAB>desc", so the gap
    // corpus can be clustered by text shape to prioritize compiler templates.
    public static int Gaps()
    {
        var db = CardDatabase.Load();
        var n = 0;
        foreach (var def in db.GetAllCards())
        {
            if (def == null) continue;
            if (WRLDZ.Duel.CardEffectStatus.Classify(def) != WRLDZ.Duel.CardEffectStatusKind.Unimplemented)
                continue;
            n++;
            var desc = (def.desc ?? "").Replace('\n', ' ').Replace('\t', ' ');
            Console.WriteLine($"{def.id}\t{def.type}\t{def.race}\t{desc}");
        }
        Console.Error.WriteLine($"# unimplemented gap cards: {n}");
        return 0;
    }

    // Regenerate the compiled-effects seed (StreamingAssets/WRLDZ/compiled_effects_seed_v1.json)
    // so the engine *remembers* current card-text compilation (compiler version-stamped)
    // instead of recompiling every card at runtime. Compiles all cards, then exports.
    public static int ExportSeed()
    {
        var db = CardDatabase.Load();
        var n = 0;
        foreach (var def in db.GetAllCards())
        {
            if (def == null) continue;
            // Fresh compile, never the seed being replaced: a same-Version seed entry would be written back unchanged,
            // so a template or enum change made without a Version bump stayed in the shipped seed (2026-10-18).
            CompiledEffectCache.ForceRecompile(def);
            n++;
        }

        // Persist only programs that carry compiled clauses — the actual card-text
        // knowledge worth remembering (0-clause vanilla / gap cards recompile trivially).
        var ok = CompiledEffectCache.ExportSeed(filter: p => p != null && p.ClauseList.Count > 0);
        Console.Error.WriteLine($"# compiled {n} cards; ExportSeed ok={ok} → {CompiledEffectCache.SeedPath}");
        return ok ? 0 : 1;
    }

    static void PrintPool(EffectCoverageService.PoolReport r)
    {
        Console.WriteLine($"  pool={r.PoolName} ids={r.UniqueIds} inDb={r.InDatabase} missing={r.MissingFromDb}");
        Console.WriteLine($"  normal={r.NormalNoEffect} fullyCompiled={r.FullyCompiled} partial={r.PartialCompiled} registry={r.RegistryOnly} gap={r.Gap}");
        Console.WriteLine($"  playable={r.PlayableCovered} ({r.PlayablePct:0.0}%)  fullCompile={r.FullCompilePct:0.0}%");
    }

    static bool IsInteresting(FieldInfo f, EffectClause c)
    {
        if (f.Name is "Timing" or "Action" or "Side" or "Zone" or "SourceSnippet") return false;
        var v = f.GetValue(c);
        if (v == null) return false;
        if (v is bool b) return b;
        if (v is int n) return n != 0;
        if (v is string s) return !string.IsNullOrEmpty(s);
        if (v.GetType().IsEnum) return Convert.ToInt64(v) != 0;
        return true;
    }

    static string Fmt(object v) => v is string s ? $"\"{s}\"" : v?.ToString() ?? "null";

    // Compiles eras/prelink_monsters.json (TCG/OCG monsters printed on or before
    // 2017-05-05) through CardTextEffectCompiler / CardEffectStatus. Does not
    // replace CardDatabase and does not write the compiled-effects seed.
    public static int Prelink()
    {
        try
        {
            return PrelinkCore();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL  prelink threw: " + e);
            return 1;
        }
    }

    const int PrelinkDarkMagician = 46986414;
    const int PrelinkDecodeTalker = 1861629;
    const int PrelinkNibiru = 27204311;

    static int PrelinkCore()
    {
        if (!TryLoadPrelink(out var file, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        CardDatabase.Load();
        var totals = new KindCounts();
        var kinds = new Dictionary<string, KindCounts>
        {
            ["Normal"] = new KindCounts(),
            ["Effect"] = new KindCounts(),
            ["Fusion"] = new KindCounts(),
            ["Synchro"] = new KindCounts(),
            ["Xyz"] = new KindCounts(),
            ["Pendulum"] = new KindCounts(),
            ["Ritual"] = new KindCounts(),
        };
        var byId = new Dictionary<int, CardDef>(file.cards.Length);
        var statusOf = new Dictionary<int, WRLDZ.Duel.CardEffectStatusKind>(file.cards.Length);
        var typeRuleStructural = 0;
        var scalesRead = 0;
        CardDef darkMagician = null;

        foreach (var row in file.cards)
        {
            if (row == null) throw new InvalidOperationException("null catalog row");
            if (row.scaleLeft > 0 || row.scaleRight > 0) scalesRead++;
            var def = row.ToDef();
            if (byId.ContainsKey(def.id))
                throw new InvalidOperationException("duplicate id " + def.id);
            byId[def.id] = def;
            WRLDZ.Duel.CardEffectStatusKind status;
            try
            {
                status = WRLDZ.Duel.CardEffectStatus.Classify(def);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"compile failed for {def.id} {def.name}", e);
            }
            statusOf[def.id] = status;
            totals.Add(status);
            if (status == WRLDZ.Duel.CardEffectStatusKind.Structural &&
                OfficialCardAuthority.HasNoActivatableEffect(def) &&
                !OfficialCardAuthority.IsNormalMonsterNoEffect(def))
                typeRuleStructural++;
            foreach (var kind in KindNames(def))
                kinds[kind].Add(status);
            if (def.id == PrelinkDarkMagician) darkMagician = def;
        }

        var failures = new List<string>();
        if (byId.ContainsKey(PrelinkDecodeTalker))
            failures.Add($"Link monster {PrelinkDecodeTalker} is in the pool");
        if (byId.ContainsKey(PrelinkNibiru))
            failures.Add($"post-wall monster {PrelinkNibiru} is in the pool");
        if (darkMagician == null)
            failures.Add($"Dark Magician {PrelinkDarkMagician} is absent");
        else if (statusOf[PrelinkDarkMagician] != WRLDZ.Duel.CardEffectStatusKind.Structural)
            failures.Add($"Dark Magician classified {statusOf[PrelinkDarkMagician]}");

        var db = CardDatabase.Load();
        CardDef agreed = null;
        var compared = 0;
        var disagreed = 0;
        var drift = 0;
        var driftSample = new List<int>();
        var inDb = 0;
        foreach (var dbDef in db.GetAllCards())
        {
            if (dbDef == null || !dbDef.IsMonster) continue;
            if (!byId.TryGetValue(dbDef.id, out var poolDef)) continue;
            inDb++;
            if ((dbDef.desc ?? "") != (poolDef.desc ?? ""))
            {
                drift++;
                if (driftSample.Count < 20) driftSample.Add(dbDef.id);
                continue;
            }
            var dbStatus = WRLDZ.Duel.CardEffectStatus.Classify(dbDef);
            if (dbStatus != WRLDZ.Duel.CardEffectStatusKind.Implemented) continue;
            compared++;
            if (agreed == null) agreed = dbDef;
            if (statusOf[dbDef.id] != dbStatus) disagreed++;
        }
        if (agreed == null)
            failures.Add("no Implemented cards_db monster has the same desc in the pool");
        if (disagreed > 0)
            failures.Add($"{disagreed} desc-matching Implemented monsters classified differently");

        var repo = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? ".";
        var reportPath = Path.Combine(repo, "Docs", "PRELINK_MONSTER_COMPILE.md");
        var report = BuildPrelinkReport(file, totals, kinds, typeRuleStructural, scalesRead, inDb,
            agreed, compared, disagreed, drift, driftSample, failures);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, report);

        Console.WriteLine(report);
        Console.WriteLine($"wrote {reportPath}");
        if (failures.Count > 0)
        {
            Console.Error.WriteLine("FAIL  prelink: " + string.Join("; ", failures));
            return 1;
        }
        return 0;
    }

    static string BuildPrelinkReport(
        PrelinkCatalogFile file,
        KindCounts totals,
        Dictionary<string, KindCounts> kinds,
        int typeRuleStructural,
        int scalesRead,
        int inDb,
        CardDef agreed,
        int compared,
        int disagreed,
        int drift,
        List<int> driftSample,
        List<string> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Pre-Link monster compile");
        sb.AppendLine();
        sb.Append("Compiler version: ").Append(CardTextEffectCompiler.Version).AppendLine();
        sb.Append("Wall: ").Append(file.wall).AppendLine();
        sb.Append("Source: ").Append(file.source).AppendLine();
        sb.Append("Pool: ").Append(totals.Total).AppendLine(" monsters");
        sb.Append("Also in cards_db: ").Append(inDb).AppendLine();
        sb.Append("Pendulum scales read: ").Append(scalesRead).AppendLine();
        sb.AppendLine();
        sb.AppendLine("| Status | Count |");
        sb.AppendLine("|---|---:|");
        sb.Append("| Structural | ").Append(totals.Structural).AppendLine(" |");
        sb.Append("| Implemented | ").Append(totals.Implemented).AppendLine(" |");
        sb.Append("| Stub | ").Append(totals.Stub).AppendLine(" |");
        sb.Append("| Unimplemented | ").Append(totals.Unimplemented).AppendLine(" |");
        sb.AppendLine();
        sb.Append("Structural includes ").Append(typeRuleStructural);
        sb.AppendLine(" monsters the current type rule treats as effectless (a Fusion, Synchro, Xyz, or Link type or frame without the word Effect). Their printed text was not compiled. That rule is unchanged.");
        sb.AppendLine();
        sb.AppendLine("| Kind | Structural | Implemented | Stub | Unimplemented | Cards |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var name in new[] { "Normal", "Effect", "Fusion", "Synchro", "Xyz", "Pendulum", "Ritual" })
        {
            var k = kinds[name];
            sb.Append("| ").Append(name).Append(" | ")
                .Append(k.Structural).Append(" | ")
                .Append(k.Implemented).Append(" | ")
                .Append(k.Stub).Append(" | ")
                .Append(k.Unimplemented).Append(" | ")
                .Append(k.Total).AppendLine(" |");
        }
        sb.AppendLine();
        sb.AppendLine("## Excluded");
        sb.AppendLine();
        sb.Append("- Link: ").Append(file.excludedLink).AppendLine();
        sb.Append("- After the wall: ").Append(file.excludedAfterWall).AppendLine();
        sb.Append("- Undated: ").Append(file.excludedUndated).AppendLine();
        sb.Append("- Missing from cards.cdb: ").Append(file.excludedMissingCdb).AppendLine();
        sb.Append("- Token: ").Append(file.excludedToken).AppendLine();
        var undated = file.excludedUndatedIds ?? Array.Empty<int>();
        if (undated.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Undated ids:");
            sb.AppendLine();
            foreach (var id in undated)
                sb.Append("- ").Append(id).AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("## Pins");
        sb.AppendLine();
        sb.Append("- Dark Magician (").Append(PrelinkDarkMagician).AppendLine("): Structural");
        sb.Append("- Decode Talker (").Append(PrelinkDecodeTalker).AppendLine(") absent");
        sb.Append("- Nibiru, the Primal Being (").Append(PrelinkNibiru).AppendLine(") absent");
        if (agreed != null)
        {
            sb.Append("- Desc-matching Implemented check: ").Append(agreed.id).Append(' ').Append(agreed.name);
            sb.Append(" (").Append(compared).Append(" compared, ").Append(disagreed).AppendLine(" disagreed)");
        }
        else
            sb.AppendLine("- Desc-matching Implemented check: none");
        sb.Append("- Desc drift vs cards_db: ").Append(drift);
        if (driftSample.Count > 0)
            sb.Append(" (first ").Append(string.Join(", ", driftSample)).Append(')');
        sb.AppendLine();
        sb.AppendLine();
        sb.Append("Result: ").AppendLine(failures.Count == 0 ? "PASS" : "FAIL");
        if (failures.Count > 0)
        {
            foreach (var f in failures)
                sb.Append("- ").AppendLine(f);
        }
        return sb.ToString();
    }

    static IEnumerable<string> KindNames(CardDef def)
    {
        var t = def.type ?? "";
        var f = def.frameType ?? "";
        bool Has(string needle) =>
            t.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
            f.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        if (Has("Normal")) yield return "Normal";
        if (Has("Effect") || f.Equals("effect", StringComparison.OrdinalIgnoreCase) ||
            f.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0)
            yield return "Effect";
        if (Has("Fusion")) yield return "Fusion";
        if (Has("Synchro")) yield return "Synchro";
        if (Has("Xyz") || Has("XYZ")) yield return "Xyz";
        if (Has("Pendulum")) yield return "Pendulum";
        if (Has("Ritual")) yield return "Ritual";
    }

    public sealed class KindCounts
    {
        public int Structural;
        public int Implemented;
        public int Stub;
        public int Unimplemented;
        public int Total => Structural + Implemented + Stub + Unimplemented;

        public void Add(WRLDZ.Duel.CardEffectStatusKind status)
        {
            switch (status)
            {
                case WRLDZ.Duel.CardEffectStatusKind.Structural: Structural++; break;
                case WRLDZ.Duel.CardEffectStatusKind.Implemented: Implemented++; break;
                case WRLDZ.Duel.CardEffectStatusKind.Stub: Stub++; break;
                default: Unimplemented++; break;
            }
        }
    }

    public sealed class PrelinkCatalogFile
    {
        public string wall;
        public string source;
        public int excludedLink;
        public int excludedAfterWall;
        public int excludedUndated;
        public int[] excludedUndatedIds;
        public int excludedMissingCdb;
        public int excludedToken;
        public PrelinkCardRow[] cards;
    }

    public sealed class PrelinkCardRow
    {
        public int id;
        public string name;
        public string type;
        public string frameType;
        public string desc;
        public int atk;
        public int def;
        public int level;
        public string race;
        public string attribute;
        public string archetype;
        public int scaleLeft;
        public int scaleRight;

        public CardDef ToDef() => new CardDef
        {
            id = id,
            name = name ?? "",
            type = type ?? "",
            frameType = frameType ?? "",
            desc = desc ?? "",
            atk = atk,
            def = def,
            level = level,
            race = race ?? "",
            attribute = attribute ?? "",
            archetype = archetype ?? "",
            scaleLeft = scaleLeft,
            scaleRight = scaleRight,
        };
    }

}
