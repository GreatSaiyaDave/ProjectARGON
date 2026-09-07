using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Pull github.com/GreatSaiyaDave/ProjectARGON into the folder Hub opened.
    /// Hub "synced" is Unity Version Control or a GitHub *link* — it does not pull.
    /// </summary>
    public static class GitHubPullMenu
    {
        const string ExpectedRepo = "GreatSaiyaDave/ProjectARGON";

        [MenuItem("WRLDZ/Get Latest from GitHub", false, 0)]
        public static void Pull()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Stop Play Mode",
                    "Exit Play Mode first (the Play button), then run this again.",
                    "OK");
                return;
            }

            var root = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(root))
            {
                Fail("Could not find the project folder.");
                return;
            }

            if (!Directory.Exists(Path.Combine(root, ".git")))
            {
                EditorUtility.DisplayDialog(
                    "This folder is not the GitHub repo",
                    "Unity Hub is opening a local copy. The green / synced mark is " +
                    "Unity Version Control (Plastic) or a GitHub *link*. It does not " +
                    "download commits Cloud Agents push to GitHub.\n\n" +
                    "One-time fix in Unity Hub:\n" +
                    "1. Close this Editor.\n" +
                    "2. Projects → Add → Add from repository.\n" +
                    "3. GitHub → " + ExpectedRepo + " → branch main.\n" +
                    "4. Open THAT new project (6000.5.10f1).\n" +
                    "5. WRLDZ → Get Latest from GitHub whenever you want updates.",
                    "OK");
                return;
            }

            if (!GitOnPath())
            {
                EditorUtility.DisplayDialog(
                    "Git is not installed",
                    "This PC needs git for in-Editor pulls.\n\n" +
                    "Linux: open Terminal (not the Unity Console) and run:\n" +
                    "sudo apt install git\n\n" +
                    "Or in Hub: Add from repository → GitHub → " + ExpectedRepo,
                    "OK");
                return;
            }

            var remote = RunGit(root, "remote get-url origin");
            if (remote.Code != 0 ||
                remote.Text.IndexOf("ProjectARGON", StringComparison.OrdinalIgnoreCase) < 0)
            {
                EditorUtility.DisplayDialog(
                    "Wrong GitHub remote",
                    "This folder's git origin is:\n" +
                    (string.IsNullOrWhiteSpace(remote.Text) ? "(none)" : remote.Text.Trim()) +
                    "\n\nCloud Agents push " + ExpectedRepo + " on GitHub. " +
                    "Hub can look synced to a different repo or to Unity Version Control.\n\n" +
                    "Hub → Add from repository → GitHub → " + ExpectedRepo + " → main.",
                    "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("WRLDZ", "Fetching GitHub main…", 0.4f);
            var fetch = RunGit(root, "fetch origin main");
            var pull = RunGit(root, "pull --ff-only origin main");
            EditorUtility.ClearProgressBar();

            if (fetch.Code != 0 || pull.Code != 0)
            {
                Fail(
                    "GitHub pull failed.\n\n" + fetch.Text + "\n" + pull.Text +
                    "\nIf this folder has local edits, Hub → Add from repository " +
                    "and open a fresh clone of " + ExpectedRepo + ".");
                return;
            }

            AssetDatabase.Refresh();
            var log = RunGit(root, "log -1 --oneline");
            var msg =
                "Pulled origin/main.\n\n" +
                log.Text.Trim() + "\n\n" +
                "Wait for scripts to compile. Title / Desktop Lab should say BUILD " +
                WrldzBuild.Stamp + ".\n" +
                "Then: WRLDZ → Lab → Open Desktop Lab App.";
            UnityEngine.Debug.Log("[WRLDZ] " + msg.Replace('\n', ' '));
            EditorUtility.DisplayDialog("GitHub updated", msg, "OK");
        }

        static bool GitOnPath()
        {
            var r = Run("git", "--version", Directory.GetCurrentDirectory());
            return r.Code == 0;
        }

        static (int Code, string Text) RunGit(string root, string args) => Run("git", args, root);

        static (int Code, string Text) Run(string file, string args, string work)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    WorkingDirectory = work,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
                using var p = Process.Start(psi);
                if (p == null) return (1, "failed to start " + file);
                var sb = new StringBuilder();
                sb.Append(p.StandardOutput.ReadToEnd());
                sb.Append(p.StandardError.ReadToEnd());
                if (!p.WaitForExit(90000))
                {
                    try { p.Kill(); } catch { /* ignore */ }
                    return (1, file + " timed out");
                }

                return (p.ExitCode, sb.ToString());
            }
            catch (Exception ex)
            {
                return (1, ex.Message);
            }
        }

        static void Fail(string msg)
        {
            UnityEngine.Debug.LogError("[WRLDZ] " + msg);
            EditorUtility.DisplayDialog("Could not update from GitHub", msg, "OK");
        }
    }
}
