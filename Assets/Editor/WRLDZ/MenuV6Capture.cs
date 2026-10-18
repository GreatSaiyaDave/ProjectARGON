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
    /// 1920×1080 still at a frozen 2.8 s with a rect dump, the route table of every control (v6 and legacy), a 10 s
    /// frame sequence of the idle (30 fps, written by this tool), the 1080×2340 legacy fallback, then WORLD MAP → the
    /// Overworld. It restores the owner's Game view size and leaves Play. It writes <c>outDir/session.done</c> or
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
        const double WatchdogSeconds = 240.0;
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

            // 2. landscape 1920×1080: still at a frozen 2.8 s, rects, routes, the 10 s idle sequence
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

            // 3. portrait 1080×2340: today's menu (the fallback), its rects and its route table
            yield return SetSize(1080, 2340);
            menu = Rebuild();
            yield return Wait(1.4f);
            if (menu.IsMenuV6)
            {
                ok = false;
                fail = (fail == null ? "" : fail + "; ") + "1080x2340 took the v6 path (expected the legacy menu)";
            }

            yield return null;
            yield return new WaitForEndOfFrame();
            Shot("port_1080x2340_legacy", 1);
            Rects("port_1080x2340_legacy", menu);
            yield return Routes(menu, routesLegacy);

            var same = new List<string>();
            var differ = new List<string>();
            foreach (var kv in routesV6)
            {
                if (!routesLegacy.TryGetValue(kv.Key, out var leg)) continue;
                (leg == kv.Value ? same : differ).Add(kv.Key + ": " + kv.Value + (leg == kv.Value ? "" : " vs legacy " + leg));
            }

            if (differ.Count > 0)
            {
                ok = false;
                fail = (fail == null ? "" : fail + "; ") + "routes differ: " + string.Join(" | ", differ);
            }

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
            j.Append(",\n  \"routesLegacy\": ").Append(Map(routesLegacy));
            j.Append(",\n  \"routesSame\": [").Append(string.Join(", ", same.ConvertAll(Q))).Append("]");
            j.Append(",\n  \"notes\": [").Append(string.Join(", ", _notes.ConvertAll(Q))).Append("]");
            j.Append(",\n  \"result\": ").Append(Q(ok ? "ok" : "fail: " + fail)).Append("\n}\n");
            try { File.WriteAllText(Path.Combine(_out, "session.json"), j.ToString()); }
            catch (Exception ex) { _notes.Add("session.json: " + ex.Message); }
            Finish(ok, fail);
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
                Close(menu);
                yield return null;
            }
        }

        static string Describe(DuelDiskMenuUI menu)
        {
            foreach (var pop in new[] { "Popover_DuelChoice", "Popover_QuitConfirm" })
                if (Find(menu.MenuCanvas, pop) != null)
                    return "popover " + pop.Substring("Popover_".Length);
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
            foreach (var pop in new[] { "Popover_DuelChoice", "Popover_QuitConfirm" })
            {
                var p = Find(menu.MenuCanvas, pop);
                if (p != null) DestroyImmediate(p.gameObject);
            }

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
        void Rects(string name, DuelDiskMenuUI menu)
        {
            var sb = new StringBuilder();
            sb.Append("{\"screen\": [").Append(Screen.width).Append(", ").Append(Screen.height)
                .Append("], \"origin\": \"bottom-left\", \"rects\": {");
            var first = true;
            var corners = new Vector3[4];
            if (menu != null && menu.MenuCanvas != null)
                foreach (var rt in menu.MenuCanvas.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                    rt.GetWorldCorners(corners);
                    var x0 = Mathf.Min(corners[0].x, corners[2].x);
                    var y0 = Mathf.Min(corners[0].y, corners[2].y);
                    var x1 = Mathf.Max(corners[0].x, corners[2].x);
                    var y1 = Mathf.Max(corners[0].y, corners[2].y);
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(Q(PathOf(rt, menu.MenuCanvas))).Append(": [").Append(F(x0)).Append(", ").Append(F(y0))
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
                var p = v?.GetType().GetProperty("selectedSizeIndex", Any);
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
