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
