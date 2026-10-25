using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WRLDZ.UI;
using WRLDZ.UI.MenuV6;
using WRLDZ.UI.Shell;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// The menu build's verify step (BUILD_PLAN §5.5, P1a):
    /// <list type="bullet">
    /// <item><b>WRLDZ → Menus → Play Main Menu</b>: one Play straight into <c>Assets/Scenes/MainMenu.unity</c>
    /// (EditorSceneManager.playModeStartScene, cleared again when Play ends), so nobody sits through the boot.</item>
    /// <item><see cref="StartSession"/>: ONE scripted Play session (every Play entry reloads the domain here): the
    /// 1920×1080 still at a frozen 2.8 s with a rect dump, P1b's hover / press / voice stills (the page's own state
    /// shots), the route table of every control, a 10 s frame sequence of the idle (30 fps, written by this tool), the
    /// 1080×2340 v6 first cut with its stills and routes, the 1080×2340 legacy fallback (MenuV6Stage.ForceLegacy) and its
    /// routes (v6 == legacy on every shared control, both plans), then WORLD MAP → the Overworld. It restores the owner's Game view size and leaves Play. It writes <c>outDir/session.done</c> or
    /// <c>outDir/session.failed</c> when Play has ended (ueval.py capture polls for them).</item>
    /// </list>
    /// Every file is written synchronously on the main thread to an absolute path outside the project (never under
    /// Assets/; ProjectWorkingDirectoryGuard). No account is created: a logged-out session goes to Boot and fails.
    /// </summary>
    [InitializeOnLoad]
    public static class MenuV6Capture
    {
        const string MainMenuScene = "Assets/Scenes/MainMenu.unity";
        const string K = "WRLDZ.MenuV6Capture.";
        const string KOneShot = K + "OneShotStartScene";
        const string KActive = K + "Active";
        const string KPhase = K + "Phase";
        const string KOut = K + "Out";
        const string KStarted = K + "Started";
        const string KStartTime = K + "StartTime";
        const string KRunStart = K + "RunStart";
        const string KResult = K + "Result";
        const string KPrevSize = K + "PrevSizeIndex";
        const string KAdded = K + "AddedSizes";
        const double WatchdogSeconds = 480.0;
        const string RunnerName = "MenuV6CaptureRunner";

        static MenuV6Capture()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        // ------------------------------------------------------------------ the one-shot Play item

        [MenuItem("WRLDZ/Menus/Play Main Menu")]
        public static void PlayMainMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorUtility.scriptCompilationFailed)
            {
                // Unity refuses Play with compile errors; setting the start scene now would leave it set for his next Play
                Debug.LogError("[WRLDZ MenuV6Capture] fix the compile errors first (Play cannot start)");
                return;
            }

            if (!SetStartScene(out var why))
            {
                Debug.LogError("[WRLDZ MenuV6Capture] " + why);
                return;
            }

            EditorApplication.EnterPlaymode();
        }

        [MenuItem("WRLDZ/Menus/Play Main Menu", true)]
        static bool PlayMainMenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static bool SetStartScene(out string why)
        {
            why = null;
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScene);
            if (scene == null)
            {
                why = MainMenuScene + " not found";
                return false;
            }

            EditorSceneManager.playModeStartScene = scene;
            SessionState.SetBool(KOneShot, true);
            return true;
        }

        // ------------------------------------------------------------------ the capture session

        /// <summary>Start the one Play session of a landing (Pipeline eval contract: StartSession(string, string)).</summary>
        public static void StartSession(string phase, string outDir)
        {
            string err = null;
            try
            {
                outDir = Path.GetFullPath(outDir ?? "");
                var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                if (string.IsNullOrEmpty(outDir) || !Path.IsPathRooted(outDir))
                    err = "outDir must be an absolute path";
                else if ((outDir + Path.DirectorySeparatorChar).StartsWith(project + Path.DirectorySeparatorChar,
                             StringComparison.Ordinal))
                    err = "outDir must be outside the project (never under Assets/)";
                if (err == null)
                {
                    Directory.CreateDirectory(outDir);
                    foreach (var f in new[] { "session.done", "session.failed", "session.json" })
                        if (File.Exists(Path.Combine(outDir, f))) File.Delete(Path.Combine(outDir, f));
                }
            }
            catch (Exception ex)
            {
                err = "outDir: " + ex.Message;
            }

            if (err == null && EditorApplication.isPlayingOrWillChangePlaymode) err = "the Editor is already in Play mode";
            if (err == null && EditorUtility.scriptCompilationFailed) err = "the Editor has compile errors (Play cannot start)";
            if (err == null && !SetStartScene(out var why)) err = why;
            if (err != null)
            {
                Debug.LogError("[WRLDZ MenuV6Capture] " + err);
                TryWrite(outDir, "session.failed", err + "\n");
                return;
            }

            SessionState.SetBool(KActive, true);
            SessionState.SetString(KPhase, phase ?? "");
            SessionState.SetString(KOut, outDir);
            SessionState.SetBool(KStarted, false);
            SessionState.SetString(KResult, "");
            SessionState.SetString(KAdded, "");
            SessionState.SetFloat(KStartTime, (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(KPrevSize, GameViewSizer.SelectedIndex());
            Debug.Log("[WRLDZ MenuV6Capture] session " + phase + " → " + outDir + " (one Play session)");
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode) TrySpawn();
            if (s != PlayModeStateChange.EnteredEditMode) return;
            // belt and braces: nothing of the session stays in the owner's edit-mode scene
            for (var guard = 0; guard < 4; guard++)
            {
                var left = GameObject.Find(RunnerName);
                if (left == null) break;
                UnityEngine.Object.DestroyImmediate(left);
            }

            if (SessionState.GetBool(KOneShot, false))
            {
                EditorSceneManager.playModeStartScene = null;
                SessionState.EraseBool(KOneShot);
            }

            if (SessionState.GetBool(KActive, false)) Finish();
        }

        static void Poll()
        {
            if (!SessionState.GetBool(KActive, false)) return;
            // Play never started: refused outright (compile errors), or requested and stuck pending (a modal dialog, an
            // Editor throttled in the background: isPlayingOrWillChangePlaymode stays true while isPlaying stays false).
            // Either way end the session and cancel the pending entry, so the capture can never start later, unasked,
            // in front of the owner. A script compile before entering Play does not count against the wait.
            var waited = EditorApplication.timeSinceStartup - SessionState.GetFloat(KStartTime, 0f);
            var pending = EditorApplication.isPlayingOrWillChangePlaymode;
            if (!EditorApplication.isPlaying && !SessionState.GetBool(KStarted, false) && !EditorApplication.isCompiling &&
                waited > (pending ? 180.0 : 90.0))
            {
                SessionState.SetString(KResult, "fail: Play mode did not start within " + (int)waited + " s" +
                                                 (pending ? " (entry was pending; cancelled)" : ""));
                if (pending) EditorApplication.ExitPlaymode();
                Finish();
                return;
            }

            if (EditorApplication.isPlaying)
            {
                TrySpawn();
                // the session's own budget starts when the runner starts (the Play-entry reload is the watchdog above)
                var t0 = SessionState.GetFloat(KRunStart, SessionState.GetFloat(KStartTime, 0f));
                if (EditorApplication.timeSinceStartup - t0 > WatchdogSeconds && string.IsNullOrEmpty(SessionState.GetString(KResult, "")))
                {
                    SessionState.SetString(KResult, "fail: watchdog (" + WatchdogSeconds + " s) — the session stalled");
                    EditorApplication.ExitPlaymode();
                }
            }
        }

        static void TrySpawn()
        {
            if (!SessionState.GetBool(KActive, false) || SessionState.GetBool(KStarted, false)) return;
            if (!EditorApplication.isPlaying) return;
            SessionState.SetBool(KStarted, true);
            SessionState.SetFloat(KRunStart, (float)EditorApplication.timeSinceStartup);
            // DontDestroyOnLoad alone carries it through WORLD MAP's scene load; no HideFlags.DontSave (an object with
            // DontSave is never cleaned up for you and could outlive Play)
            var go = new GameObject(RunnerName);
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MenuV6CaptureRunner>().Begin(SessionState.GetString(KPhase, ""),
                SessionState.GetString(KOut, ""));
        }

        internal static void RunnerDone(string result)
        {
            SessionState.SetString(KResult, result ?? "fail: no result");
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            };
        }

        internal static void NoteAddedSize(int totalIndex)
        {
            var s = SessionState.GetString(KAdded, "");
            SessionState.SetString(KAdded, s.Length == 0 ? totalIndex.ToString() : s + "," + totalIndex);
        }

        static void Finish()
        {
            var outDir = SessionState.GetString(KOut, "");
            var result = SessionState.GetString(KResult, "");
            var notes = new StringBuilder();
            try
            {
                // restore the owner's size first (it was recorded before any size was added, so its index is below every
                // added one and stays valid), then remove the custom sizes this session added (highest index first), so
                // the Game view never points at a removed entry
                var prev = SessionState.GetInt(KPrevSize, -1);
                if (prev >= 0 && !GameViewSizer.Select(prev)) notes.AppendLine("could not restore game view size #" + prev);

                var added = SessionState.GetString(KAdded, "");
                if (added.Length > 0)
                {
                    var idx = new List<int>();
                    foreach (var p in added.Split(','))
                        if (int.TryParse(p, out var i)) idx.Add(i);
                    idx.Sort();
                    idx.Reverse();
                    foreach (var i in idx)
                        if (!GameViewSizer.RemoveCustom(i)) notes.AppendLine("could not remove custom game view size #" + i);
                }
            }
            catch (Exception ex)
            {
                notes.AppendLine("restore: " + ex.Message);
            }

            // Play never ran (the watchdog above): EnteredEditMode will not come, so clear the one-shot start scene here,
            // or the owner's next own Play would open the main menu instead of his scene
            if (!EditorApplication.isPlaying && SessionState.GetBool(KOneShot, false))
            {
                EditorSceneManager.playModeStartScene = null;
                SessionState.EraseBool(KOneShot);
            }

            if (string.IsNullOrEmpty(result)) result = "fail: Play mode ended before the session finished";
            var ok = result.StartsWith("ok", StringComparison.Ordinal);
            TryWrite(outDir, ok ? "session.done" : "session.failed", result + "\n" + notes);
            foreach (var k in new[] { KActive, KStarted })
                SessionState.EraseBool(k);
            foreach (var k in new[] { KPhase, KOut, KResult, KAdded })
                SessionState.EraseString(k);
            SessionState.EraseInt(KPrevSize);
            SessionState.EraseFloat(KStartTime);
            SessionState.EraseFloat(KRunStart);
            Debug.Log("[WRLDZ MenuV6Capture] " + (ok ? "done" : "FAILED") + ": " + result);
        }

        static void TryWrite(string dir, string name, string text)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, name), text ?? "");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6Capture] could not write " + name + ": " + ex.Message);
            }
        }
    }

    /// <summary>The Play-mode half of a capture session (a coroutine on a DontSave object).</summary>
    public sealed class MenuV6CaptureRunner : MonoBehaviour
    {
        string _phase, _out;
        readonly StringBuilder _json = new();
        readonly List<string> _notes = new();
        readonly List<string> _shots = new();
        bool _first = true;

        public void Begin(string phase, string outDir)
        {
            _phase = phase;
            _out = outDir;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var ok = true;
            string fail = null;
            var routesV6 = new SortedDictionary<string, string>();
            var routesLegacy = new SortedDictionary<string, string>();

            // 1. the main menu (MainMenuBootstrap builds it; a logged-out session goes to Boot)
            DuelDiskMenuUI menu = null;
            var t0 = Time.realtimeSinceStartup;
            while (menu == null && Time.realtimeSinceStartup - t0 < 20f)
            {
                menu = FindAnyObjectByType<DuelDiskMenuUI>();
                yield return null;
            }

            if (menu == null)
            {
                Finish(false, SceneManager.GetActiveScene().name == "Boot"
                    ? "no session: MainMenuBootstrap returned to Boot (log in once from Boot; this tool creates no account)"
                    : "the main menu did not appear within 20 s (scene " + SceneManager.GetActiveScene().name + ")");
                yield break;
            }

            var screen0 = Screen.width + "x" + Screen.height;
            var routesPort = new SortedDictionary<string, string>();

            // 2. landscape 1920×1080: still at a frozen 2.8 s, rects, the hover / press / voice stills (P1b), routes, the
            //    10 s idle sequence
            yield return SetSize(1920, 1080);
            menu = Rebuild();
            yield return Wait(1.4f);
            if (!menu.IsMenuV6)
            {
                ok = false;
                fail = "1920x1080 built the legacy menu: " + MenuV6Stage.WhyNot(MenuAspect.Current);
            }

            MenuV6Clock.Freeze(2.8f);
            yield return null;
            yield return new WaitForEndOfFrame();
            Shot("land_1920x1080_t2.8", 1);
            Rects("land_1920x1080_t2.8", menu);
            Rigs("land", menu);
            yield return States("land", menu);
            yield return Routes(menu, routesV6);

            menu = Rebuild();   // routes may leave a sheet behind: start the recording from a clean build
            yield return Wait(1.4f);
            var seq = Path.Combine(_out, "idle_10s");
            Directory.CreateDirectory(seq);
            const int fps = 30;
            for (var i = 0; i < 10 * fps; i++)
            {
                MenuV6Clock.Freeze(i / (float)fps);
                yield return null;
                yield return new WaitForEndOfFrame();
                Frame(Path.Combine(seq, "f" + i.ToString("000") + ".jpg"));
            }

            MenuV6Clock.Unfreeze();

            // 3. portrait 1080×2340: the v6 first cut (P1b) at a frozen 2.8 s, its rects, states and routes; then today's
            //    legacy menu (the fallback, reached with the test-only ForceLegacy) and its routes
            yield return SetSize(1080, 2340);
            menu = Rebuild();
            yield return Wait(1.4f);
            if (!menu.IsMenuV6)
            {
                ok = false;
                fail = (fail == null ? "" : fail + "; ") + "1080x2340 built the legacy menu: " + MenuV6Stage.WhyNot(MenuAspect.Current);
            }

            MenuV6Clock.Freeze(2.8f);
            yield return null;
            yield return new WaitForEndOfFrame();
            Shot("port_1080x2340_t2.8", 1);
            Rects("port_1080x2340_t2.8", menu);
            Rigs("port", menu);
            yield return States("port", menu);
            yield return Routes(menu, routesPort);
            MenuV6Clock.Unfreeze();

            MenuV6Stage.ForceLegacy = true;
            try
            {
                menu = Rebuild();
            }
            finally
            {
                MenuV6Stage.ForceLegacy = false;
            }

            yield return Wait(1.4f);
            if (menu.IsMenuV6)
            {
                ok = false;
                fail = (fail == null ? "" : fail + "; ") + "the forced legacy build took the v6 path";
            }

            yield return null;
            yield return new WaitForEndOfFrame();
            Shot("port_1080x2340_legacy", 1);
            Rects("port_1080x2340_legacy", menu);
            yield return Routes(menu, routesLegacy);

            var same = new List<string>();
            var differ = new List<string>();
            foreach (var (label, table) in new[] { ("land", routesV6), ("port", routesPort) })
                foreach (var kv in table)
                {
                    if (!routesLegacy.TryGetValue(kv.Key, out var leg)) continue;
                    // P3: a recess opens its ledger sheet first; its CTA is the route legacy takes at once ("sheet X → Y")
                    var arrow = kv.Value.LastIndexOf(" → ", StringComparison.Ordinal);
                    var v6 = arrow >= 0 ? kv.Value.Substring(arrow + 3) : kv.Value;
                    if (leg == v6) same.Add(label + " " + kv.Key + ": " + kv.Value);
                    else differ.Add(label + " " + kv.Key + ": " + kv.Value + " vs legacy " + leg);
                }

            if (differ.Count > 0)
            {
                ok = false;
                fail = (fail == null ? "" : fail + "; ") + "routes differ: " + string.Join(" | ", differ);
            }

            // P2 (BUILD_PLAN §6 P2 verify): the title stills, the boot handoff stills + a 30 fps handoff sequence (hrpop.py),
            // and the cold open, both plans. P3 shoots them again: P3 fixes the P2 landing's defects (the carve rim at handoff
            // u 1.7-1.95, the cold open at .3 / .9, TOUCH / the credit line, the video -> title cut)
            if (!string.IsNullOrEmpty(_phase) && (_phase.StartsWith("p2") || _phase.StartsWith("p3")))
                yield return P2Shots();
            // P3 (BUILD_PLAN §6 P3 verify): the Stone's sheets at fixed sheet times, both plans, and the false door over an overlay
            if (!string.IsNullOrEmpty(_phase) && (_phase.StartsWith("p3") || _phase.StartsWith("p4")))
                yield return P3Shots();
            // P4 (BUILD_PLAN §6 P4 verify): the offering hall, both plans
            if (!string.IsNullOrEmpty(_phase) && _phase.StartsWith("p4"))
                yield return P4Shots();

            // 4. WORLD MAP last: it leaves the scene
            yield return SetSize(1920, 1080);
            menu = Rebuild();
            yield return Wait(1.0f);
            var map = Find(menu.MenuCanvas, "MapBack");
            var mapRoute = "missing";
            if (map != null)
            {
                Click(map.gameObject);
                var tm = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != "Overworld" && Time.realtimeSinceStartup - tm < 15f)
                    yield return null;
                mapRoute = "scene " + SceneManager.GetActiveScene().name;
            }

            routesV6["MapBack"] = mapRoute;
            if (mapRoute != "scene Overworld")
            {
                ok = false;
                fail = (fail == null ? "" : fail + "; ") + "WORLD MAP → " + mapRoute;
            }

            // 5. session.json, then leave Play
            var j = new StringBuilder();
            j.Append("{\n  \"phase\": ").Append(Q(_phase)).Append(",\n  \"unity\": ").Append(Q(Application.unityVersion));
            j.Append(",\n  \"screenAtStart\": ").Append(Q(screen0));
            j.Append(",\n  \"shots\": [").Append(string.Join(", ", _shots.ConvertAll(Q))).Append("]");
            j.Append(",\n  \"recording\": {\"dir\": ").Append(Q(seq)).Append(", \"fps\": ").Append(fps)
                .Append(", \"t0\": 0, \"t1\": 10, \"scale\": 0.5, \"format\": \"jpg\"}");
            j.Append(",\n  \"routesV6\": ").Append(Map(routesV6));
            j.Append(",\n  \"routesV6Port\": ").Append(Map(routesPort));
            j.Append(",\n  \"rigs\": ").Append(Map(_rigs));
            j.Append(",\n  \"routesLegacy\": ").Append(Map(routesLegacy));
            j.Append(",\n  \"routesSame\": [").Append(string.Join(", ", same.ConvertAll(Q))).Append("]");
            j.Append(",\n  \"notes\": [").Append(string.Join(", ", _notes.ConvertAll(Q))).Append("]");
            j.Append(",\n  \"result\": ").Append(Q(ok ? "ok" : "fail: " + fail)).Append("\n}\n");
            try { File.WriteAllText(Path.Combine(_out, "session.json"), j.ToString()); }
            catch (Exception ex) { _notes.Add("session.json: " + ex.Message); }
            Finish(ok, fail);
        }

        /// <summary>
        /// P2: for 1920×1080 and 1080×2340 — the live title (TitleStage on its own canvas, the clock frozen, the menu torn
        /// down first: its opaque canvas sorts above the title's 49) at raw 21.6 (the live title's first frame = the Genesis
        /// clip's last, frame 648: the video-end check), 22.2 (the Tears start), 22.9 / 24.8 / 24.9 (land; port 21.6 / 24.9:
        /// the approved stills v63_title_16b / 17 / 18, v63_portrait_05; out_p2/probe title_land_page_*.png); the boot handoff as
        /// the page's boot cut composes it (the title left at raw 24.6, frozen at 24.6 + u, gone from u 1.5; the menu in its
        /// #enter mode at u) at u 0 / .6 / 1 / 1.5 / 1.8 / 2.3 / 2.6 / 2.95 (out_p2/handoff/&lt;layout&gt;/ref_u*.png,
        /// v63_handoff_02 / 02b / 02c / 03) and as a 30 fps sequence u 0 → 3.2; the cold open at .3 / .9 / 1.6 / 2.8
        /// (v63_menu_00a / b / c). The menu is rebuilt at rest afterwards.
        /// </summary>
        IEnumerator P2Shots()
        {
            foreach (var (w, h, lay) in new[] { (1920, 1080, "land"), (1080, 2340, "port") })
            {
                yield return SetSize(w, h);
                var kind = w >= h ? MenuLayoutKind.Landscape : MenuLayoutKind.Portrait;
                ClearMenu();   // the menu's canvas (sort 50, opaque) would cover the title canvas (49)
                // P3: the Genesis clip's own end into the live title (the held last frame fading over the live one)
                if (_phase.StartsWith("p3")) yield return GenesisCut(kind, lay);
                RectTransform tc = null;
                TitleStage ts = null;
                try
                {
                    tc = WrldzTheme.Canvas("TitleCaptureCanvas", 49, aspect: true);
                    ts = TitleStage.Build(tc, kind, new TitleStage.Options { Video = false, StartRaw = TitleStage.GenEndRaw });
                }
                catch (Exception ex)
                {
                    _notes.Add(lay + " title: " + ex.Message);
                }

                if (ts != null)
                {
                    foreach (var raw in lay == "land" ? new[] { 21.6, 22.2, 22.9, 24.8, 24.9 } : new[] { 21.6, 24.9 })
                    {
                        ts.Freeze(raw);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        Shot("title_" + lay + "_raw" + raw.ToString("0.0", CultureInfo.InvariantCulture), 1);
                    }

                    if (ts.Missing.Count > 0) _notes.Add(lay + " title missing: " + string.Join(", ", ts.Missing));
                }

                // the boot handoff (D8): the kept title under the menu's #enter mode
                if (ts != null) ts.Leave(24.6);
                MenuV6Clock.Freeze(0f);   // an entrance built on a clock frozen past its end finishes on its first frame
                var menu = RebuildWith(MenuEntry.Mode.FromTitle);
                yield return Wait(.6f);
                // (P3: + u 1.7 / 1.75 / 1.85 / 1.9 / 1.95, the carve rim's window, where the P2 landing showed an ellipse)
                foreach (var u in new[] { 0, .6f, 1f, 1.5f, 1.7f, 1.75f, 1.8f, 1.85f, 1.9f, 1.95f, 2.3f, 2.6f, 2.95f })
                {
                    MenuV6Clock.Freeze(u);
                    if (ts != null) ts.Freeze(24.6 + u);
                    if (tc != null) tc.gameObject.SetActive(u < 1.5f);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("handoff_" + lay + "_u" + u.ToString("0.00", CultureInfo.InvariantCulture), 1);
                }

                var seq = Path.Combine(_out, "handoff_" + lay + "_30fps");
                Directory.CreateDirectory(seq);
                for (var i = 0; i <= 96; i++)
                {
                    var u = i / 30f;
                    MenuV6Clock.Freeze(u);
                    if (ts != null) ts.Freeze(24.6 + u);
                    if (tc != null) tc.gameObject.SetActive(u < 1.5f);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Frame(Path.Combine(seq, "f" + i.ToString("000") + ".jpg"));
                }

                if (tc != null) Destroy(tc.gameObject);
                // the cold open (D10: the first-time path's world load, a duel's return); the clock was left frozen at the
                // handoff's 3.2 s, past the cold open's 3.0 s end: start it at 0
                MenuV6Clock.Freeze(0f);
                menu = RebuildWith(MenuEntry.Mode.ColdOpen);
                yield return Wait(.6f);
                foreach (var t in new[] { .3f, .9f, 1.6f, 2.8f })
                {
                    MenuV6Clock.Freeze(t);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("cold_" + lay + "_t" + t.ToString("0.0", CultureInfo.InvariantCulture), 1);
                }

                MenuV6Clock.Unfreeze();
                Rebuild();
                yield return Wait(.4f);
            }
        }

        /// <summary>
        /// P3 (the P2 landing's video -> title step, fixed by GenesisVideo.HoldAndFade): the real clip played to its natural end
        /// into the live title, on a live clock. The clip is sought to 20.9 s once it shows a frame (no 21 s wait); frames are
        /// written from raw 21.2 until 0.8 s after the title went live (genesis_&lt;layout&gt;_cut/, timings in t.csv: wall s
        /// since live, raw, live, the held frame's alpha), plus stills just before the cut and 0.2 / 0.45 s after it. Skipped
        /// with a note when the local clip is missing (a clone) or fails.
        /// </summary>
        IEnumerator GenesisCut(MenuLayoutKind kind, string lay)
        {
            if (TitleStage.GenesisClip(kind) == null)
            {
                _notes.Add(lay + " genesis cut: no local clip (MenuV6Local), not shot");
                yield break;
            }

            MenuV6Clock.Unfreeze();
            RectTransform tc = null;
            TitleStage ts = null;
            try
            {
                tc = WrldzTheme.Canvas("TitleCutCanvas", 49, aspect: true);
                ts = TitleStage.Build(tc, kind, new TitleStage.Options { Video = true, StartRaw = TitleStage.GenEndRaw });
            }
            catch (Exception ex)
            {
                _notes.Add(lay + " genesis cut: " + ex.Message);
            }

            if (ts == null || ts.Video == null)
            {
                _notes.Add(lay + " genesis cut: the title built without its clip");
                if (tc != null) Destroy(tc.gameObject);
                yield break;
            }

            // the clip's first frame, then the seek near its end
            var t0 = Time.realtimeSinceStartup;
            while (ts.Video != null && !ts.IsLive && ts.Video.Raw <= 0 && Time.realtimeSinceStartup - t0 < 8f) yield return null;
            var vp = ts.Video != null ? ts.Video.GetComponent<UnityEngine.Video.VideoPlayer>() : null;
            if (vp != null && !ts.IsLive) vp.time = 20.9;
            var seq = Path.Combine(_out, "genesis_" + lay + "_cut");
            Directory.CreateDirectory(seq);
            var csv = new StringBuilder("i,wall_since_live,raw,live,held_alpha\n");
            var liveAt = -1f;
            var pre = false;
            var post2 = false;
            var post45 = false;
            var n = 0;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 26f)
            {
                yield return new WaitForEndOfFrame();
                var now = Time.realtimeSinceStartup;
                if (ts.IsLive && liveAt < 0) liveAt = now;
                if (ts.Raw < 21.2 && !ts.IsLive) continue;
                var held = GameObject.Find("GenesisVideo");
                var heldImg = held != null ? held.GetComponent<RawImage>() : null;
                var since = liveAt < 0 ? -1f : now - liveAt;
                if (!ts.IsLive && ts.Raw >= 21.45 && !pre)
                {
                    pre = true;
                    Shot("genesis_" + lay + "_cut_pre_raw" + ts.Raw.ToString("0.00", CultureInfo.InvariantCulture), 1);
                }
                else if (since >= .2f && !post2)
                {
                    post2 = true;
                    Shot("genesis_" + lay + "_cut_live+0.20", 1);
                }
                else if (since >= .45f && !post45)
                {
                    post45 = true;
                    Shot("genesis_" + lay + "_cut_live+0.45", 1);
                }

                Frame(Path.Combine(seq, "f" + n.ToString("000") + ".jpg"));
                csv.Append(n).Append(',').Append(since.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                    .Append(ts.Raw.ToString("0.000", CultureInfo.InvariantCulture)).Append(',').Append(ts.IsLive ? 1 : 0).Append(',')
                    .Append(heldImg != null ? heldImg.color.a.ToString("0.000", CultureInfo.InvariantCulture) : "-").Append('\n');
                n++;
                if (since >= .8f) break;
            }

            File.WriteAllText(Path.Combine(seq, "t.csv"), csv.ToString());
            if (liveAt < 0) _notes.Add(lay + " genesis cut: the title never went live in 26 s (" + n + " frames)");
            if (tc != null) Destroy(tc.gameObject);
            yield return null;
        }

        /// <summary>
        /// P3: for 1920×1080 and 1080×2340 — each Stone sheet held at fixed sheet times (StoneSheet.ShowAt; the Stone's clock
        /// frozen at 2.8): CHOOSE YOUR DUEL u .35 / .8 / 1.3 / 1.9 / 2.6 / 4 (the frame chunks, the cards dealt and turned:
        /// v63_menu_03 / 03b, v63_portrait_00b), the five ledgers at rest u 3 and STORY's opening u .5 / 1 / 1.6 (v63_menu_08,
        /// v63_relics_03, v63_portrait_00c), the Ring seek u .4 / .9 / 1.5 / 2 / 2.35 (never past RingSheet.GoAt: it would leave
        /// the scene; v63_relics_04 / 04b / 04c), QUIT u .6 / 3; each sheet's close at v .4 / .9 / 1.5 (DUEL) or .15 / .45 / .9
        /// (the 1.35x sheets; StoneSheet.ShowCloseAt) and the Stone once it is gone ("_closed": must equal the t2.8 still); DUEL's
        /// open + close as a 30 fps sequence (land, the pop scan); then on a live clock the BAZAAR CTA's overlay in the false door
        /// at rest and SETTINGS at rest and .25 s into its close (the FrameGhost). The menu is rebuilt at rest afterwards.
        /// </summary>
        IEnumerator P3Shots()
        {
            foreach (var (w, h, lay) in new[] { (1920, 1080, "land"), (1080, 2340, "port") })
            {
                yield return SetSize(w, h);
                MenuV6Clock.Freeze(2.8f);
                var menu = Rebuild();
                yield return Wait(.6f);
                if (!menu.IsMenuV6)
                {
                    _notes.Add("p3 " + lay + ": the legacy menu built; no sheet shots");
                    continue;
                }

                // P4 (the P3 landing's compare): the sheet times of the approved stills themselves — v63_menu_03 at u 3.4, the
                // ledgers (story v63_menu_08, deck v63_relics_03) at u 1.2, the Ring 04 / 04b / 04c at u 1.9 / 2.2 / 2.4 (2.4 is
                // RingSheet.GoAt, where the seek holds and leaves the scene: shot a hair before it, the same held frame)
                var plan = new (string control, string id, double[] us)[]
                {
                    ("Hero", "duel", new[] { .35, .8, 1.3, 1.9, 2.6, 3.4, 4.0 }),
                    ("Dest_DECK", "deck", new[] { 1.2, 3.0 }),
                    ("Dest_BAG", "bag", new[] { 3.0 }),
                    ("Dest_STORY", "story", new[] { .5, 1.0, 1.2, 1.6, 3.0 }),
                    ("Dest_BAZAAR", "bazaar", new[] { 3.0 }),
                    ("Tert_VIEW", "view", new[] { 3.0 }),
                    ("MapBack", "ring", new[] { .4, .9, 1.5, 1.9, 2.0, 2.2, 2.35, RingSheet.GoAt - .001 }),
                    ("Quit", "quit", new[] { .6, 3.0 })
                };
                foreach (var (control, id, us) in plan)
                {
                    var c = Find(menu.MenuCanvas, control);
                    if (c == null)
                    {
                        _notes.Add("p3 " + lay + " " + id + ": control " + control + " missing");
                        continue;
                    }

                    Click(c.gameObject);
                    yield return null;
                    var sheet = menu.Sheet;
                    if (sheet == null)
                    {
                        _notes.Add("p3 " + lay + " " + id + ": no sheet opened");
                        Close(menu);
                        continue;
                    }

                    foreach (var u in us)
                    {
                        if (id == "ring" && u >= RingSheet.GoAt) continue;
                        sheet.ShowAt(u);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        Shot("p3_" + lay + "_" + id + "_u" + u.ToString("0.00", CultureInfo.InvariantCulture), 1);
                    }

                    // DUEL (land): the open u 0 -> 2.6 and the close v 0 -> 1.9 as a 30 fps sequence, for the pop scan (§6 P3 verify)
                    if (id == "duel" && lay == "land")
                    {
                        var dseq = Path.Combine(_out, "p3_land_duel_30fps");
                        Directory.CreateDirectory(dseq);
                        var fi = 0;
                        for (var i = 0; i <= 78; i++)
                        {
                            sheet.ShowAt(i / 30.0);
                            yield return null;
                            yield return new WaitForEndOfFrame();
                            Frame(Path.Combine(dseq, "f" + (fi++).ToString("000") + ".jpg"));
                        }

                        for (var i = 1; i <= 57; i++)
                        {
                            sheet.ShowCloseAt(2.6 + i / 30.0, i / 30.0);
                            yield return null;
                            yield return new WaitForEndOfFrame();
                            Frame(Path.Combine(dseq, "f" + (fi++).ToString("000") + ".jpg"));
                        }
                    }

                    // the close at fixed close times (frozen clock), then the Stone once the sheet is gone (it must equal the
                    // t2.8 still: keystone, room DOM, torch and the door all back at rest)
                    var uRest = us[us.Length - 1];
                    if (id == "ring" && uRest >= RingSheet.GoAt) uRest = RingSheet.GoAt - .05;
                    foreach (var v in id == "duel" ? new[] { .4, .9, 1.5 } : new[] { .15, .45, .9 })
                    {
                        sheet.ShowCloseAt(id == "ring" ? uRest : uRest + v, v);   // the Ring closes from its held frame
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        Shot("p3_" + lay + "_" + id + "_close_v" + v.ToString("0.00", CultureInfo.InvariantCulture), 1);
                    }

                    Close(menu);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("p3_" + lay + "_" + id + "_closed", 1);
                }

                // on a live clock: the false door over an overlay (the BAZAAR ledger's CTA), SETTINGS and its close
                MenuV6Clock.Unfreeze();
                var bz = Find(menu.MenuCanvas, "Dest_BAZAAR");
                if (bz != null)
                {
                    Click(bz.gameObject);
                    yield return null;
                    var cta = Find(menu.MenuCanvas, "Cta");
                    if (cta != null)
                    {
                        var b = cta.GetComponent<Button>();
                        if (b != null) b.onClick.Invoke();
                        yield return Wait(2.6f);
                        yield return new WaitForEndOfFrame();
                        Shot("p3_" + lay + "_bazaar_overlay_rest", 1);
                    }
                    else _notes.Add("p3 " + lay + ": the BAZAAR ledger has no Cta");

                    Close(menu);
                    yield return null;
                }

                var set = Find(menu.MenuCanvas, "Tert_SET");
                if (set != null)
                {
                    Click(set.gameObject);
                    yield return Wait(2.6f);
                    yield return new WaitForEndOfFrame();
                    Shot("p3_" + lay + "_settings_rest", 1);
                    Transform close = null;
                    foreach (var shell in FindObjectsByType<MenuShell>())
                        if (shell != null && shell.OverlayOnly && shell.ModalHost != null && close == null)
                            close = Find(shell.ModalHost, "Close");
                    var cb = close != null ? close.GetComponent<Button>() : null;
                    if (cb != null)
                    {
                        cb.onClick.Invoke();
                        yield return Wait(.25f);
                        yield return new WaitForEndOfFrame();
                        Shot("p3_" + lay + "_settings_close_0.25", 1);
                        yield return Wait(2.3f);
                    }
                    else _notes.Add("p3 " + lay + ": SETTINGS has no Close");

                    Close(menu);
                }

                MenuV6Clock.Unfreeze();
                Rebuild();
                yield return Wait(.4f);
            }
        }

        /// <summary>
        /// P4: for 1920×1080 and 1080×2340 — the deck builder (the offering hall) opened through the hub's overlay shell
        /// (MenuShell.ShowOverlay(DeckCollection), the DECK ledger's route), on the owner's real decks, held at hall times: the
        /// collection at 1.5 s (v63_deck_01, the entrance done) and the editor at rest (6.8 s, v63_deck_04's time), the inspect
        /// sheet on the first collection card at rest (v63_deck_03), the +1 flight drawn on its own (HallProbe.FlyPreview:
        /// no TryAdd) at u .2 / .4 / .6 and the landing burst at .9, a search with chips (an EXCLUDE word typed through the
        /// field + the MONSTER chip, in memory; the chip pop at .2 and at rest), the deck select (the Caret's tap opens the
        /// on-hand deck list; no deck is switched), DRAW 5 open, the box shut the way SAVE shuts it (.2 / .32 / 1.77 s:
        /// v63_deck_05, shot without saving; HallProbe.Seal) and the lid lifting again as a deck select lifts it (.1 / .25 / .5).
        /// The hall is closed by ClearOverlays (never through the back chip: no save).
        /// </summary>
        IEnumerator P4Shots()
        {
            foreach (var (w, h, lay) in new[] { (1920, 1080, "land"), (1080, 2340, "port") })
            {
                yield return SetSize(w, h);
                var menu = Rebuild();
                yield return Wait(.6f);
                MenuShell shell = null;
                foreach (var s in FindObjectsByType<MenuShell>())
                    if (s != null && s.OverlayOnly && shell == null)
                        shell = s;
                if (shell == null)
                {
                    _notes.Add("p4 " + lay + ": no overlay shell");
                    continue;
                }

                shell.ShowOverlay(MenuId.DeckCollection);
                yield return Wait(.3f);
                var hall = shell.ModalHost != null ? shell.ModalHost.GetComponentInChildren<DeckHall>(true) : null;
                var probe = hall != null ? hall.GetComponent<DeckCollectionScreen.HallProbe>() : null;
                if (hall == null || probe == null)
                {
                    _notes.Add("p4 " + lay + ": the deck builder did not build the hall: " + DeckHall.WhyNot());
                    shell.ClearOverlays();
                    continue;
                }

                foreach (var at in new[] { 1.5, 6.8 })
                {
                    hall.FreezeAt(at);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("p4_" + lay + "_deck_t" + at.ToString("0.00", CultureInfo.InvariantCulture), 1);
                }

                MenuV6Clock.Unfreeze();
                if (probe.ShowFirstCard())
                {
                    hall.FreezeAt(hall.Now + 2.0);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("p4_" + lay + "_deck_inspect", 1);
                    MenuV6Clock.Unfreeze();
                    // the +1 flight, drawn only (no TryAdd: the deck is not touched)
                    var tf = hall.Now;
                    if (probe.FlyPreview())
                    {
                        foreach (var du in new[] { .2, .4, .6, .9 })
                        {
                            hall.FreezeAt(tf + du);
                            yield return null;
                            yield return new WaitForEndOfFrame();
                            Shot("p4_" + lay + "_deck_plus1_u" + du.ToString("0.00", CultureInfo.InvariantCulture), 1);
                        }

                        MenuV6Clock.Unfreeze();
                        yield return Wait(1.6f);
                    }
                    else _notes.Add("p4 " + lay + ": no +1 flight (no MAIN slot)");

                    probe.HideCard();
                    yield return Wait(1.6f);
                }
                else _notes.Add("p4 " + lay + ": no collection card to inspect");

                // a search with chips (in memory: the EXCLUDE word through the field, the MONSTER chip)
                var tq = hall.Now;
                var q = probe.SearchPreview();
                if (q != null)
                {
                    foreach (var du in new[] { .2, 1.2 })
                    {
                        hall.FreezeAt(tq + du);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        Shot("p4_" + lay + "_deck_search_" + du.ToString("0.0", CultureInfo.InvariantCulture), 1);
                    }

                    MenuV6Clock.Unfreeze();
                    probe.ClearSearch();
                    yield return Wait(.6f);
                }
                else _notes.Add("p4 " + lay + ": nothing to search (empty collection)");

                // the deck select: the Caret's own tap opens the on-hand deck list (no deck is picked)
                var caret = Find(hall.transform, "Caret");
                if (caret != null)
                {
                    var ped = new PointerEventData(EventSystem.current);
                    ExecuteEvents.Execute(caret.gameObject, ped, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(caret.gameObject, ped, ExecuteEvents.pointerUpHandler);
                    yield return Wait(.5f);
                    hall.FreezeAt(hall.Now);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("p4_" + lay + "_deck_select", 1);
                    MenuV6Clock.Unfreeze();
                    if (Find(hall.transform, "DeckDropdown") == null) _notes.Add("p4 " + lay + ": the Caret opened no DeckDropdown");
                    ped = new PointerEventData(EventSystem.current);
                    ExecuteEvents.Execute(caret.gameObject, ped, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(caret.gameObject, ped, ExecuteEvents.pointerUpHandler);
                    yield return Wait(.4f);
                }
                else _notes.Add("p4 " + lay + ": no Caret");

                if (probe.DrawFive(true))
                {
                    hall.FreezeAt(hall.Now + 2.0);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("p4_" + lay + "_deck_draw5", 1);
                    MenuV6Clock.Unfreeze();
                    probe.DrawFive(false);
                }
                else _notes.Add("p4 " + lay + ": MAIN is empty (no DRAW 5 shot)");

                probe.Seal();
                var t0 = hall.Now;
                foreach (var dt in new[] { .2, .32, 1.77 })
                {
                    hall.FreezeAt(t0 + dt);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("p4_" + lay + "_deck_sealed_" + dt.ToString("0.00", CultureInfo.InvariantCulture), 1);
                }

                MenuV6Clock.Unfreeze();
                // the lid lifts again, as a deck select (or the next edit) lifts it
                probe.Unseal();
                var tl = hall.Now;
                foreach (var dv in new[] { .1, .25, .5 })
                {
                    hall.FreezeAt(tl + dv);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Shot("p4_" + lay + "_deck_lid_" + dv.ToString("0.00", CultureInfo.InvariantCulture), 1);
                }

                MenuV6Clock.Unfreeze();
                Rects("p4_" + lay + "_deck", hall.transform);
                shell.ClearOverlays();
                yield return null;
            }

            Rebuild();
            yield return Wait(.4f);
        }

        /// <summary>Tear the menu down (its overlays, host and canvas), as <see cref="Rebuild"/> does before it builds.</summary>
        static void ClearMenu()
        {
            foreach (var shell in FindObjectsByType<MenuShell>())
                if (shell != null && shell.OverlayOnly)
                    shell.ClearOverlays();
            foreach (var n in new[] { "MainMenuHost", "DiskMenuCanvas" })
                for (var guard = 0; guard < 8; guard++)
                {
                    var go = GameObject.Find(n);
                    if (go == null) break;
                    DestroyImmediate(go);
                }
        }

        /// <summary>Rebuild the menu entered by <paramref name="entry"/> (P2: the handoff / the cold open).</summary>
        static DuelDiskMenuUI RebuildWith(MenuEntry.Mode entry)
        {
            ClearMenu();
            var host = new GameObject("MainMenuHost");
            var menu = host.AddComponent<DuelDiskMenuUI>();
            menu.Entry = entry;
            menu.Build();
            return menu;
        }

        void Finish(bool ok, string fail)
        {
            MenuV6Clock.Unfreeze();
            MenuV6Capture.RunnerDone(ok ? "ok " + _shots.Count + " shots" : "fail: " + fail);
        }

        // ------------------------------------------------------------------ steps

        IEnumerator SetSize(int w, int h)
        {
            if (!GameViewSizer.Set(w, h, out var added, out var why)) _notes.Add("game view " + w + "x" + h + ": " + why);
            if (added >= 0) MenuV6Capture.NoteAddedSize(added);
            yield return Wait(0.6f);
            if (Screen.width != w || Screen.height != h)
                _notes.Add("asked " + w + "x" + h + ", Screen is " + Screen.width + "x" + Screen.height);
        }

        static IEnumerator Wait(float seconds)
        {
            var t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < seconds) yield return null;
        }

        /// <summary>Tear down the menu and build it again for the current surface (v6 / legacy is decided at Build),
        /// the way MainMenuBootstrap does.</summary>
        static DuelDiskMenuUI Rebuild()
        {
            foreach (var shell in FindObjectsByType<MenuShell>())
                if (shell != null && shell.OverlayOnly)
                    shell.ClearOverlays();
            foreach (var n in new[] { "MainMenuHost", "DiskMenuCanvas" })
                for (var guard = 0; guard < 8; guard++)
                {
                    var go = GameObject.Find(n);
                    if (go == null) break;
                    DestroyImmediate(go);
                }

            var host = new GameObject("MainMenuHost");
            var menu = host.AddComponent<DuelDiskMenuUI>();
            menu.Build();
            return menu;
        }

        readonly SortedDictionary<string, string> _rigs = new();

        /// <summary>Which rigs run and which kept their P1a still (session.json "rigs").</summary>
        void Rigs(string layout, DuelDiskMenuUI menu)
        {
            var stone = menu != null ? menu.Stone : null;
            if (stone == null || stone.Life == null)
            {
                _rigs[layout] = "no v6 stage";
                return;
            }

            var names = new List<string>();
            foreach (var r in stone.Life.Rigs) names.Add(r.Name);
            _rigs[layout] = "running: " + string.Join(" ", names) + (stone.RigsKept.Count > 0 ? "; kept the P1a still: " + string.Join(" ", stone.RigsKept) : "");
        }

        // the page's full-page state shots (out_p1b/rigs/<layout>/shots): every control hovered (settled), two presses
        // (one recess, the door) 0.1 s old, and THE PHARAOH'S VOICE at u .2 / .9 / 1.5 / 2.0 of its typing cycle.
        // QUIT (the DOM hover plate) and the Ring echo (glow + sway) have no page shot: they are shot too, so every hover
        // state the owner can reach has a still (checked by eye against the page's hover rules).
        static readonly (string control, string id)[] HoverShots =
        {
            ("Dest_DECK", "deck"), ("Dest_BAG", "bag"), ("Dest_STORY", "story"), ("Dest_BAZAAR", "bazaar"), ("Tert_VIEW", "view"),
            ("Tert_SET", "set"), ("Hero", "door"), ("DeckPlaque", "box"), ("MapBack", "map"), ("Quit", "quit"),
            ("RingEcho", "ring")
        };

        IEnumerator States(string layout, DuelDiskMenuUI menu)
        {
            var stone = menu != null ? menu.Stone : null;
            if (stone == null) yield break;
            MenuV6Clock.Freeze(2.8f);
            foreach (var (control, id) in HoverShots)
            {
                if (!stone.ForceState(control, 9, double.NaN))
                {
                    _notes.Add(layout + " hover " + control + ": no state");
                    continue;
                }

                yield return Wait(0.5f);   // the CSS transitions run on wall-clock time (the page's shots wait 450 ms)
                yield return new WaitForEndOfFrame();
                Shot(layout + "_hov_" + id + "_t2.8", 1);
                stone.ForceState(control, double.NaN, double.NaN);
            }

            foreach (var (control, id) in new[] { ("Dest_DECK", "deck"), ("Hero", "door") })
            {
                if (!stone.ForceState(control, 9, 0.1)) continue;
                yield return Wait(0.5f);
                yield return new WaitForEndOfFrame();
                Shot(layout + "_press_" + id + "_a0.10_t2.8", 1);
                stone.ForceState(control, double.NaN, double.NaN);
            }

            yield return Wait(0.5f);
            foreach (var u in new[] { 0.2f, 0.9f, 1.5f, 2.0f })
            {
                MenuV6Clock.Freeze(28f + u);
                yield return null;
                yield return new WaitForEndOfFrame();
                Shot(layout + "_voice_u" + u.ToString("0.000", CultureInfo.InvariantCulture), 1);
            }

            MenuV6Clock.Freeze(2.8f);
            yield return null;
        }

        IEnumerator Routes(DuelDiskMenuUI menu, IDictionary<string, string> table)
        {
            foreach (var name in new[]
                     {
                         "Hero", "Dest_DECK", "Dest_BAG", "Dest_STORY", "Dest_BAZAAR", "Tert_VIEW", "Tert_SET", "Quit",
                         "DeckPlaque", "FaceHost"
                     })
            {
                var t = Find(menu.MenuCanvas, name);
                if (t == null)
                {
                    if (name != "DeckPlaque") table[name] = "missing";
                    continue;
                }

                Click(t.gameObject);
                yield return null;
                yield return null;
                table[name] = Describe(menu);
                // P3: a recess ledger's CTA is today's route (the legacy menu takes it at once)
                if (table[name].StartsWith("sheet Recess_", StringComparison.Ordinal))
                {
                    var cta = Find(menu.MenuCanvas, "Cta");
                    var cb = cta != null ? cta.GetComponent<Button>() : null;
                    if (cb != null)
                    {
                        cb.onClick.Invoke();
                        yield return null;
                        yield return null;
                        table[name] += " → " + Describe(menu);
                    }
                }

                Close(menu);
                yield return null;
            }
        }

        static string Describe(DuelDiskMenuUI menu)
        {
            foreach (var pop in new[] { "Popover_DuelChoice", "Popover_QuitConfirm" })
                if (Find(menu.MenuCanvas, pop) != null)
                    return "popover " + pop.Substring("Popover_".Length);
            // P3: a ledger / Ring sheet over the Stone
            var sheet = menu.Sheet;
            if (sheet != null && sheet.Host != null)
                return "sheet " + sheet.Host.name.Replace("Popover_", "");
            foreach (var shell in FindObjectsByType<MenuShell>())
            {
                if (shell == null || !shell.OverlayOnly || shell.ModalHost == null || shell.ModalHost.childCount == 0) continue;
                var first = shell.ModalHost.GetChild(0);
                return "overlay " + first.name;
            }

            var av = FindAnyObjectByType<AvatarCustomizerUI>();
            if (av != null && av.isActiveAndEnabled) return "avatar customizer";
            return "nothing opened";
        }

        static void Close(DuelDiskMenuUI menu)
        {
            // every popover / v6.3 sheet host (P3: Popover_Recess_*, Popover_WorldMap); a sheet put back the Stone as it goes
            var pops = new List<GameObject>();
            var root = menu != null ? menu.MenuCanvas : null;
            if (root != null)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name.StartsWith("Popover_", StringComparison.Ordinal))
                        pops.Add(t.gameObject);
            foreach (var p in pops)
                if (p != null) DestroyImmediate(p);

            foreach (var shell in FindObjectsByType<MenuShell>())
                if (shell != null && shell.OverlayOnly)
                    shell.ClearOverlays();
            var av = FindAnyObjectByType<AvatarCustomizerUI>();
            if (av != null) av.HideAll();
        }

        static void Click(GameObject go)
        {
            var es = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            var ped = new PointerEventData(es) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
        }

        static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = Find(root.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }

        void Shot(string name, int scale)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture(Mathf.Max(1, scale));
            try
            {
                var path = Path.Combine(_out, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                _shots.Add(path);
            }
            catch (Exception ex)
            {
                _notes.Add(name + ": " + ex.Message);
            }
            finally
            {
                Destroy(tex);
            }
        }

        /// <summary>One recording frame at half size (2×2 box filter on the CPU; no RenderTexture), as JPEG.</summary>
        void Frame(string path)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            Texture2D half = null;
            try
            {
                int w = tex.width / 2, h = tex.height / 2;
                var src = tex.GetPixels32();
                var dst = new Color32[w * h];
                var sw = tex.width;
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var a = src[(2 * y) * sw + 2 * x];
                    var b = src[(2 * y) * sw + 2 * x + 1];
                    var c = src[(2 * y + 1) * sw + 2 * x];
                    var d = src[(2 * y + 1) * sw + 2 * x + 1];
                    dst[y * w + x] = new Color32((byte)((a.r + b.r + c.r + d.r + 2) >> 2),
                        (byte)((a.g + b.g + c.g + d.g + 2) >> 2), (byte)((a.b + b.b + c.b + d.b + 2) >> 2), 255);
                }

                half = new Texture2D(w, h, TextureFormat.RGB24, false);
                half.SetPixels32(dst);
                half.Apply(false);
                File.WriteAllBytes(path, half.EncodeToJPG(88));
                if (_first)
                {
                    _first = false;
                    _shots.Add(path);
                }
            }
            catch (Exception ex)
            {
                _notes.Add("frame " + Path.GetFileName(path) + ": " + ex.Message);
            }
            finally
            {
                Destroy(tex);
                if (half != null) Destroy(half);
            }
        }

        /// <summary>Every named RectTransform under the menu canvas → screen rect, bottom-left origin
        /// ({"screen":[W,H],"origin":"bottom-left","rects":{path:[x,y,w,h]}}, the compare.py contract).</summary>
        void Rects(string name, DuelDiskMenuUI menu) => Rects(name, menu != null ? menu.MenuCanvas : null);

        /// <summary>Every named RectTransform's screen rect under <paramref name="root"/> (P4: the hall's own root).</summary>
        void Rects(string name, Transform root)
        {
            var sb = new StringBuilder();
            sb.Append("{\"screen\": [").Append(Screen.width).Append(", ").Append(Screen.height)
                .Append("], \"origin\": \"bottom-left\", \"rects\": {");
            var first = true;
            var corners = new Vector3[4];
            if (root != null)
                foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                    rt.GetWorldCorners(corners);
                    var x0 = Mathf.Min(corners[0].x, corners[2].x);
                    var y0 = Mathf.Min(corners[0].y, corners[2].y);
                    var x1 = Mathf.Max(corners[0].x, corners[2].x);
                    var y1 = Mathf.Max(corners[0].y, corners[2].y);
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(Q(PathOf(rt, root))).Append(": [").Append(F(x0)).Append(", ").Append(F(y0))
                        .Append(", ").Append(F(x1 - x0)).Append(", ").Append(F(y1 - y0)).Append("]");
                }

            sb.Append("}}\n");
            try { File.WriteAllText(Path.Combine(_out, name + ".rects.json"), sb.ToString()); }
            catch (Exception ex) { _notes.Add(name + " rects: " + ex.Message); }
        }

        static string PathOf(Transform t, Transform root)
        {
            var s = t.name;
            var p = t.parent;
            while (p != null && t != root)
            {
                s = p.name + "/" + s;
                if (p == root) break;
                p = p.parent;
            }

            return s;
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }

            return sb.Append('"').ToString();
        }

        static string Map(IDictionary<string, string> d)
        {
            var parts = new List<string>();
            foreach (var kv in d) parts.Add(Q(kv.Key) + ": " + Q(kv.Value));
            return "{" + string.Join(", ", parts) + "}";
        }
    }

    /// <summary>
    /// The Game view's size list through reflection (internal Editor API: GameViewSizes / GameViewSizeGroup /
    /// GameView.selectedSizeIndex; BUILD_PLAN §5.5 "guess: to be proven in P1"). Every call fails soft: a false
    /// return is logged in session.json and the capture continues at whatever size the view has.
    /// </summary>
    static class GameViewSizer
    {
        static readonly Assembly Ed = typeof(Editor).Assembly;
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static EditorWindow View()
        {
            var t = Ed.GetType("UnityEditor.GameView");
            if (t == null) return null;
            var all = Resources.FindObjectsOfTypeAll(t);
            return all.Length > 0 ? all[0] as EditorWindow : null;
        }

        static object Group()
        {
            var sizes = Ed.GetType("UnityEditor.GameViewSizes");
            if (sizes == null) return null;
            var single = typeof(ScriptableSingleton<>).MakeGenericType(sizes);
            var inst = single.GetProperty("instance", Any)?.GetValue(null);
            return inst == null ? null : sizes.GetProperty("currentGroup", Any)?.GetValue(inst);
        }

        public static int SelectedIndex()
        {
            try
            {
                var v = View();
                var p = v != null ? v.GetType().GetProperty("selectedSizeIndex", Any) : null;
                return p != null ? (int)p.GetValue(v) : -1;
            }
            catch
            {
                return -1;
            }
        }

        public static bool Select(int index)
        {
            try
            {
                var v = View();
                if (v == null || index < 0) return false;
                var m = v.GetType().GetMethod("SizeSelectionCallback", Any);
                if (m != null)
                {
                    m.Invoke(v, new object[] { index, null });
                    return true;
                }

                var p = v.GetType().GetProperty("selectedSizeIndex", Any);
                if (p == null || !p.CanWrite) return false;
                p.SetValue(v, index);
                v.Repaint();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Select a fixed w×h size, adding a custom one when the list has none. <paramref name="added"/> is the
        /// new entry's index (to remove at the end) or −1.</summary>
        public static bool Set(int w, int h, out int added, out string why)
        {
            added = -1;
            why = null;
            try
            {
                var g = Group();
                if (g == null)
                {
                    why = "GameViewSizes.currentGroup not reachable";
                    return false;
                }

                var gt = g.GetType();
                var count = (int)gt.GetMethod("GetTotalCount", Any).Invoke(g, null);
                var get = gt.GetMethod("GetGameViewSize", Any);
                var sizeType = Ed.GetType("UnityEditor.GameViewSizeType");
                var fixedRes = Enum.Parse(sizeType, "FixedResolution");
                for (var i = 0; i < count; i++)
                {
                    var s = get.Invoke(g, new object[] { i });
                    var st = s.GetType();
                    if ((int)st.GetProperty("width", Any).GetValue(s) == w &&
                        (int)st.GetProperty("height", Any).GetValue(s) == h &&
                        Equals(st.GetProperty("sizeType", Any).GetValue(s), fixedRes))
                        return Select(i) || Fail("could not select #" + i, out why);
                }

                var gvs = Ed.GetType("UnityEditor.GameViewSize");
                var size = Activator.CreateInstance(gvs, fixedRes, w, h, "WRLDZ capture " + w + "x" + h);
                gt.GetMethod("AddCustomSize", Any).Invoke(g, new[] { size });
                added = count;
                return Select(count) || Fail("added #" + count + " but could not select it", out why);
            }
            catch (Exception ex)
            {
                why = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public static bool RemoveCustom(int totalIndex)
        {
            try
            {
                var g = Group();
                if (g == null) return false;
                g.GetType().GetMethod("RemoveCustomSize", Any).Invoke(g, new object[] { totalIndex });
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool Fail(string msg, out string why)
        {
            why = msg;
            return false;
        }
    }
}
