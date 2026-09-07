// Headless entrypoint: runs the WRLDZ rules-engine regression + stress suites
// outside the Unity Editor and prints their reports. Exits non-zero if any
// suite reports a failure (a line beginning with "FAIL  ").
using System;
using System.IO;
using System.Text;

internal static class Program
{
    static int Main(string[] args)
    {
        // Point the shimmed UnityEngine.Application at the repo's StreamingAssets.
        var root = FindRepoRoot();
        var streaming = Path.Combine(root, "Assets", "StreamingAssets");
        UnityEngine.Application.streamingAssetsPath = streaming;
        UnityEngine.Application.dataPath = Path.Combine(root, "Assets");
        Directory.CreateDirectory(UnityEngine.Application.persistentDataPath);

        bool quiet = Array.IndexOf(args, "--quiet") >= 0;
        if (quiet) UnityEngine.Debug.Silence = true;

        Console.WriteLine("== WRLDZ headless engine test harness ==");
        Console.WriteLine("streamingAssetsPath = " + streaming);
        Console.WriteLine();

        var sb = new StringBuilder();
        int suites = 0, failedSuites = 0;

        suites += RunSuite(sb, "TcgRegressionTests", () => WRLDZ.Duel.Rules.TcgRegressionTests.RunAll(), ref failedSuites);
        suites += RunSuite(sb, "InteractionRegressionTests", () => WRLDZ.Duel.Rules.InteractionRegressionTests.RunAll(), ref failedSuites);
        suites += RunSuite(sb, "CorpusTriggerStressTests", () => WRLDZ.Duel.Rules.CorpusTriggerStressTests.Run(), ref failedSuites);

        var report = sb.ToString();
        Console.WriteLine(report);

        // A failing line in any suite report ("FAIL  ") fails the run.
        bool anyFail = report.IndexOf("FAIL  ", StringComparison.Ordinal) >= 0 || failedSuites > 0;
        Console.WriteLine();
        Console.WriteLine($"== SUMMARY: {suites} suite(s) run, {failedSuites} threw, result={(anyFail ? "FAIL" : "PASS")} ==");
        return anyFail ? 1 : 0;
    }

    static int RunSuite(StringBuilder sb, string name, Func<string> run, ref int failedSuites)
    {
        sb.AppendLine("######## " + name + " ########");
        try
        {
            var r = run();
            sb.AppendLine(r);
        }
        catch (Exception e)
        {
            failedSuites++;
            sb.AppendLine("FAIL  " + name + " threw: " + e);
        }
        sb.AppendLine();
        return 1;
    }

    static string FindRepoRoot()
    {
        var env = Environment.GetEnvironmentVariable("WRLDZ_REPO_ROOT");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(Path.Combine(env, "Assets"))) return env;
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "Assets", "StreamingAssets")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        // Fallback: assume the csproj lives at <root>/Tools/HeadlessEngine.
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
