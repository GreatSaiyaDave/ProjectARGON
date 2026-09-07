// Card inspector: prints how a card's official text compiles into an effect
// program. Useful when authoring/adding cards and for diagnosing why a card is
// classified unimplemented/stub or refuses to activate.
//
//   Tools/HeadlessEngine/run.sh --card "Call of the Haunted"
//   Tools/HeadlessEngine/run.sh --card 97077563
using System;
using System.Linq;
using System.Reflection;
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
        Console.WriteLine($"  type={def.type}  race={def.race}  attr={def.attribute}  {(def.IsMonster ? $"Lv{def.level} {def.atk}/{def.def}" : "")}");
        Console.WriteLine($"  desc: {def.desc}");
        Console.WriteLine();

        var status = WRLDZ.Duel.CardEffectStatus.Classify(def);
        Console.WriteLine($"  classify: {status}");
        Console.WriteLine($"  ProgramMayActivate: {OfficialEffectRegistry.ProgramMayActivate(def)}");

        var prog = CardTextEffectCompiler.Compile(def);
        if (prog == null) { Console.WriteLine("  compile: <null program>"); return 0; }
        Console.WriteLine($"  FullyCompiled: {prog.FullyCompiled}");
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
        }
        return 0;
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
            CompiledEffectCache.GetOrCompile(def);
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
}
