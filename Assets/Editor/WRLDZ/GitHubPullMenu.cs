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
        const string UnstickCurl =
            "curl -fsSL https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/Tools/unstick_merge_keep_github.sh | bash";

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
                    "This folder is not a git copy yet",
                    "Hub linked GitHub as a bookmark. It did not download commits.\n\n" +
                    "Keep THIS folder. Close Unity. Open Linux Terminal " +
                    "(not this Console). Type cd, space, drag this ProjectARGON " +
                    "folder onto the Terminal, Enter, then paste:\n\n" +
                    "curl -fsSL https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/Tools/get_latest_into_this_folder.sh | bash\n\n" +
                    "That updates this folder in place and keeps local work.",
                    "OK");
                return;
            }

            if (!GitOnPath())
            {
                EditorUtility.DisplayDialog(
                    "Git is not installed",
                    "This PC needs git for in-Editor pulls.\n\n" +
                    "Linux: open Terminal (not the Unity Console) and run:\n" +
                    "sudo apt install git",
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
                    "Do not Add a second Hub project. Close Unity and run " +
                    "Tools/get_latest_into_this_folder.sh from Linux Terminal " +
                    "in this folder (see GET_THE_GAME.txt).",
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
                    "\nIf this folder has local edits, the pull stopped on purpose. " +
                    "Do not zip-replace. Close Unity and run " +
                    "Tools/get_latest_into_this_folder.sh (see GET_THE_GAME.txt).");
                return;
            }

            AssetDatabase.Refresh();
            var log = RunGit(root, "log -1 --oneline");
            var msg =
                "Pulled origin/main.\n\n" +
                log.Text.Trim() + "\n\n" +
                "Wait for scripts to compile. Project search WRLDZ_BUILD should say " +
                WrldzBuild.Stamp + ". Console: [WRLDZ] BUILD " + WrldzBuild.Stamp + ".\n" +
                "Then: WRLDZ → Lab → Open Desktop Lab App.";
            UnityEngine.Debug.Log("[WRLDZ] " + msg.Replace('\n', ' '));
            EditorUtility.DisplayDialog("GitHub updated", msg, "OK");
        }

        [MenuItem("WRLDZ/Send this folder to GitHub", false, 1)]
        public static void Send()
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
                    "This folder is not a git copy yet",
                    "Hub linked GitHub as a bookmark. It did not download commits.\n\n" +
                    "Keep THIS folder. Close Unity. Open Linux Terminal " +
                    "(not this Console). Type cd, space, drag this ProjectARGON " +
                    "folder onto the Terminal, Enter, then paste:\n\n" +
                    "curl -fsSL https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/Tools/send_this_folder_to_github.sh | bash\n\n" +
                    "That saves this folder, keeps GitHub's files, then uploads.",
                    "OK");
                return;
            }

            if (!GitOnPath())
            {
                EditorUtility.DisplayDialog(
                    "Git is not installed",
                    "This PC needs git to send this folder.\n\n" +
                    "Linux: open Terminal (not the Unity Console) and run:\n" +
                    "sudo apt install git",
                    "OK");
                return;
            }

            if (File.Exists(Path.Combine(root, ".git", "MERGE_HEAD"))
                || Directory.Exists(Path.Combine(root, "Assets", "StreamingAssets", "OcgCore", "scripts", "official~")))
            {
                Fail(
                    "This folder is stuck from the last send. Nothing was deleted. GitHub was not overwritten.\n\n" +
                    "Close Unity, then paste this in Terminal inside ProjectARGON:\n\n" +
                    UnstickCurl,
                    "Could not send to GitHub");
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
                    "\n\nStopped so we do not upload the wrong project.",
                    "OK");
                return;
            }

            EnsureGitIdentity(root);

            EditorUtility.DisplayProgressBar("WRLDZ", "Saving this folder…", 0.2f);
            RunGit(root, "add -A");
            var staged = RunGit(root, "diff --cached --quiet");
            if (staged.Code != 0)
            {
                var commit = RunGit(
                    root,
                    "commit -m \"Local Unity Hub folder " + DateTime.UtcNow.ToString("yyyy-MM-dd") + "\"");
                if (commit.Code != 0)
                {
                    EditorUtility.ClearProgressBar();
                    Fail("Could not save local files.\n\n" + commit.Text, "Could not send to GitHub");
                    return;
                }
            }

            EditorUtility.DisplayProgressBar("WRLDZ", "Combining with GitHub…", 0.55f);
            var fetch = RunGit(root, "fetch origin");
            var mergeBase = RunGit(root, "merge-base HEAD origin/main");
            var mergeArgs = mergeBase.Code == 0
                ? "merge origin/main --no-edit"
                : "merge origin/main --allow-unrelated-histories --no-edit";
            var merge = RunGit(root, mergeArgs);
            if (fetch.Code != 0 || merge.Code != 0)
            {
                RunGit(root, "merge --abort");
                EditorUtility.ClearProgressBar();
                Fail(
                    "Stopped so nothing is wiped. GitHub and this folder both changed the same files.\n\n" +
                    "Close Unity, then paste this in Terminal inside ProjectARGON:\n\n" +
                    UnstickCurl + "\n\n" +
                    fetch.Text + "\n" + merge.Text,
                    "Could not send to GitHub");
                return;
            }

            var local = RunGit(root, "rev-parse HEAD");
            var remoteHead = RunGit(root, "rev-parse origin/main");
            if (local.Code == 0 &&
                remoteHead.Code == 0 &&
                string.Equals(local.Text.Trim(), remoteHead.Text.Trim(), StringComparison.Ordinal))
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "Already synced",
                    "This folder already matches GitHub. Nothing new to upload.",
                    "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("WRLDZ", "Uploading to GitHub…", 0.85f);
            var push = RunGit(root, "push origin main");
            EditorUtility.ClearProgressBar();
            if (push.Code != 0)
            {
                Fail(
                    "Your files are saved in this folder. GitHub did not accept the upload " +
                    "(sign-in needed).\n\nClose Unity. Open Linux Terminal in this folder and run:\n\n" +
                    "sudo apt install gh\n" +
                    "gh auth login\n\n" +
                    "Choose GitHub.com, HTTPS, Login with a web browser.\n" +
                    "Then: WRLDZ → Send this folder to GitHub again.\n\n" +
                    push.Text,
                    "Could not send to GitHub");
                return;
            }

            AssetDatabase.Refresh();
            var log = RunGit(root, "log -1 --oneline");
            var msg =
                "Uploaded to GitHub main.\n\n" +
                log.Text.Trim() + "\n\n" +
                "Keep using this same folder. Do not Add a second Hub copy.";
            UnityEngine.Debug.Log("[WRLDZ] " + msg.Replace('\n', ' '));
            EditorUtility.DisplayDialog("Sent to GitHub", msg, "OK");
        }

        static void EnsureGitIdentity(string root)
        {
            var email = RunGit(root, "config user.email");
            if (email.Code != 0 || string.IsNullOrWhiteSpace(email.Text))
            {
                RunGit(root, "config user.email owner@localhost");
                RunGit(root, "config user.name \"WRLDZ owner\"");
            }
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

        static void Fail(string msg, string title = "Could not update from GitHub")
        {
            UnityEngine.Debug.LogError("[WRLDZ] " + msg);
            EditorUtility.DisplayDialog(title, msg, "OK");
        }
    }
}
