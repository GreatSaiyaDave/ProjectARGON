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

        int cardArg = Array.IndexOf(args, "--card");
        if (cardArg >= 0 && cardArg + 1 < args.Length)
        {
            UnityEngine.Debug.Silence = true;
            return Diag.InspectCard(args[cardArg + 1]);
        }

        if (Array.IndexOf(args, "--coverage") >= 0)
        {
            UnityEngine.Debug.Silence = true;
            return Diag.Coverage();
        }

        if (Array.IndexOf(args, "--gaps") >= 0)
        {
            UnityEngine.Debug.Silence = true;
            return Diag.Gaps();
        }

        int duels = ReadIntArg(args, "--duels", WRLDZ.Duel.Rules.DuelEngineStressTests.DefaultDuelCount);

        Console.WriteLine("== WRLDZ headless engine test harness ==");
        Console.WriteLine("streamingAssetsPath = " + streaming);
        Console.WriteLine($"stress duels = {duels}");
        Console.WriteLine();

        // The comprehensive stress suite runs the unit regressions
        // (TcgRegressionTests + InteractionRegressionTests + CorpusTriggerStressTests),
        // the lab-deck text-compile check, `duels` complete AI-vs-AI games, and a
        // battle-math fuzz. `Ok` is false on any unit fail, exception, or soft-lock.
        WRLDZ.Duel.Rules.DuelEngineStressTests.StressReport report;
        try
        {
            report = WRLDZ.Duel.Rules.DuelEngineStressTests.Run(duels);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL  DuelEngineStressTests threw: " + e);
            return 1;
        }

        Console.WriteLine(report.Summary);
        Console.WriteLine();
        Console.WriteLine($"== SUMMARY: unitPass={report.UnitPass} unitFail={report.UnitFail} " +
                          $"duelsPlayed={report.DuelsPlayed} completed={report.DuelsCompleted} " +
                          $"turnCap={report.DuelsTurnCapped} softLocks={report.SoftLocksRecovered} " +
                          $"exceptions={report.Exceptions} wins(P/O/D)={report.PlayerWins}/{report.OppWins}/{report.DrawsOrCap} " +
                          $"result={(report.Ok ? "PASS" : "FAIL")} ==");
        return report.Ok ? 0 : 1;
    }

    static int ReadIntArg(string[] args, string name, int fallback)
    {
        int i = Array.IndexOf(args, name);
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) && v > 0) return v;
        return fallback;
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
