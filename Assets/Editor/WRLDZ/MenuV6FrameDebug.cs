using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// P8 (PERF_PLAN.md step 0): the batcher's own numbers for one frozen frame, from the Frame Debugger. The API is
    /// internal (UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility in this Editor's UnityEditor.CoreModule,
    /// 6000.5.10f1: SetEnabled, count, limit, GetFrameEvents, GetFrameEventData, GetBatchBreakCauseStrings), so it is reached by
    /// reflection and every miss degrades to a note, never a throw.
    /// <para>What the Frame Debugger window does, scripted: Play is paused, the debugger enabled for the Editor
    /// (ProfilerDriver.connectedProfiler), the event list read once it is stable, then the limit is stepped through every event
    /// (the Game view repaints, the replayed frame stops at the limit and the native side fills that event's data: shader,
    /// pass, vertex / index / draw-call counts, the batch-break cause, the textures, the render target). Then the limit is
    /// restored, the debugger disabled and Play resumed. All of it runs in EditorApplication.update (a paused Play does not
    /// run the capture's coroutine, which waits on <see cref="Job.Done"/>); <see cref="Abort"/> (the capture's Finish and its
    /// watchdog) always disables it and resumes Play.</para>
    /// </summary>
    public static class MenuV6FrameDebug
    {
        static readonly Assembly Ed = typeof(Editor).Assembly;
        static Type _fdu, _data, _evt;

        public sealed class Job
        {
            public string Label;
            public bool Done;
            public string Json = "null";
            public string Note;
            internal double Started, StepAt, Deadline;
            internal int Index = -1, Count, Stable, LastCount = -1, Timeouts;
            internal bool PausedByUs, Enabled, Kicked;
            internal Array Events;
            internal object Data;
            internal readonly List<string> Rows = new();
            internal readonly Dictionary<string, int> Causes = new(), Textures = new(), Shaders = new(), Types = new();
            internal int Draws, DrawCalls, Verts;
        }

        static Job _job;

        /// <summary>True when this Editor exposes the Frame Debugger's utility (and can debug locally).</summary>
        public static bool Available(out string why)
        {
            why = null;
            try
            {
                if (_fdu == null) _fdu = Ed.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
                if (_data == null) _data = Ed.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");
                if (_evt == null) _evt = Ed.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEvent");
                if (_fdu == null || _data == null || _evt == null)
                {
                    why = "FrameDebuggerUtility / FrameDebuggerEventData not found in " + Ed.GetName().Name;
                    return false;
                }

                foreach (var m in new[] { "SetEnabled", "GetFrameEvents", "GetFrameEventData", "GetBatchBreakCauseStrings" })
                    if (_fdu.GetMethod(m, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) == null)
                    {
                        why = "FrameDebuggerUtility." + m + " missing";
                        return false;
                    }

                var ls = Prop("locallySupported");
                if (ls is bool b && !b)
                {
                    why = "FrameDebuggerUtility.locallySupported is false";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                why = "reflection: " + ex.Message;
                return false;
            }
        }

        static object Prop(string name)
        {
            var p = _fdu.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return p != null ? p.GetValue(null) : null;
        }

        static void SetProp(string name, object v)
        {
            var p = _fdu.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (p != null && p.CanWrite) p.SetValue(null, v);
        }

        static object Call(string name, params object[] args)
        {
            foreach (var m in _fdu.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                if (m.Name == name && m.GetParameters().Length == args.Length)
                    return m.Invoke(null, args);
            throw new MissingMethodException("FrameDebuggerUtility." + name);
        }

        static int Count => Prop("count") is int c ? c : 0;

        static int EditorPlayerId()
        {
            try
            {
                var pd = Ed.GetType("UnityEditorInternal.ProfilerDriver");
                var p = pd != null ? pd.GetProperty("connectedProfiler", BindingFlags.Public | BindingFlags.Static) : null;
                if (p != null && p.GetValue(null) is int id) return id;
            }
            catch
            {
                // the Editor's own connection is the default
            }

            return 0;
        }

        static int RemoteId()
        {
            try { return Call("GetRemotePlayerGUID") is int id ? id : 0; }
            catch { return 0; }
        }

        /// <summary>Start reading the frame now on screen (null + <paramref name="why"/> when the debugger is unavailable or a
        /// job runs). <paramref name="seconds"/> bounds the whole job; what was read by then is kept.</summary>
        public static Job Begin(string label, double seconds, out string why)
        {
            if (_job != null && !_job.Done)
            {
                why = "a frame debugger job is running (" + _job.Label + ")";
                return null;
            }

            if (!Available(out why)) return null;
            var j = new Job
            {
                Label = label,
                Started = EditorApplication.timeSinceStartup,
                Deadline = EditorApplication.timeSinceStartup + Math.Max(5, seconds)
            };
            try
            {
                j.Data = Activator.CreateInstance(_data);
                if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                {
                    EditorApplication.isPaused = true;
                    j.PausedByUs = true;
                }

                Call("SetEnabled", true, EditorPlayerId());
                j.Enabled = true;
            }
            catch (Exception ex)
            {
                j.Note = "enable: " + Inner(ex);
                End(j);
                why = j.Note;
                return j;
            }

            _job = j;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Repaint();
            return j;
        }

        /// <summary>Stop any job: the debugger off, Play resumed if this paused it (safe to call at any time); and the
        /// debugger off even when no job runs (a disable that failed must not leave the Game view showing a replay).</summary>
        public static void Abort(string why = "aborted")
        {
            var j = _job;
            if (j != null && !j.Done)
            {
                j.Note = (j.Note == null ? "" : j.Note + "; ") + why;
                End(j);
            }

            ForceOff();
        }

        /// <summary>True when the Frame Debugger is still on; then it is switched off (the window's own disable call).</summary>
        public static bool ForceOff()
        {
            try
            {
                if (!FrameDebugger.enabled || _fdu == null) return false;
                Call("SetEnabled", false, RemoteId());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6FrameDebug] disable: " + Inner(ex));
                return true;
            }
        }

        static string Inner(Exception ex) => ex is TargetInvocationException t && t.InnerException != null ? t.InnerException.Message : ex.Message;

        static MethodInfo _sceneDirty;

        /// <summary>What the Frame Debugger window does on a limit change (EditorApplication.SetSceneRepaintDirty + a
        /// repaint), plus the Game view's own repaint (the replay draws there). Never steps the player loop.</summary>
        static void Repaint()
        {
            try
            {
                if (_sceneDirty == null)
                    _sceneDirty = typeof(EditorApplication).GetMethod("SetSceneRepaintDirty", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (_sceneDirty != null) _sceneDirty.Invoke(null, null);
                var gv = Ed.GetType("UnityEditor.GameView");
                if (gv != null)
                    foreach (var o in Resources.FindObjectsOfTypeAll(gv))
                    {
                        var w = o as EditorWindow;
                        if (w != null) w.Repaint();
                    }
            }
            catch
            {
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        static void Tick()
        {
            var j = _job;
            if (j == null || j.Done)
            {
                EditorApplication.update -= Tick;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            try
            {
                if (now > j.Deadline)
                {
                    j.Note = (j.Note == null ? "" : j.Note + "; ") + "deadline: read " + Math.Max(0, j.Index) + " of " + j.Count + " events";
                    End(j);
                    return;
                }

                if (!EditorApplication.isPlaying)
                {
                    j.Note = (j.Note == null ? "" : j.Note + "; ") + "Play ended";
                    End(j);
                    return;
                }

                if (j.Events == null)
                {
                    // wait for a stable event list (the window waits four repaints)
                    var c = Count;
                    j.Stable = c > 0 && c == j.LastCount ? j.Stable + 1 : 0;
                    j.LastCount = c;
                    if (j.Stable < 4)
                    {
                        if (now - j.Started > 20)
                        {
                            j.Note = "no frame events after 20 s (count " + c + ")";
                            End(j);
                            return;
                        }

                        Repaint();
                        return;
                    }

                    j.Events = Call("GetFrameEvents") as Array;
                    j.Count = j.Events != null ? Math.Min(j.Events.Length, c) : 0;
                    j.Index = 0;
                    j.StepAt = now;
                    SetProp("limit", 1);
                    Repaint();
                    return;
                }

                if (j.Index >= j.Count)
                {
                    End(j);
                    return;
                }

                // the event at Index: its data arrives once the replay stopped at limit = Index + 1
                var ok = Call("GetFrameEventData", j.Index, j.Data) is bool b && b;
                var fi = Field(j.Data, "m_FrameEventIndex") is int x ? x : -1;
                if (ok && fi == j.Index)
                {
                    Record(j, j.Index, true);
                    Next(j, now);
                    return;
                }

                if (now - j.StepAt > .5 && !j.Kicked)
                {
                    // the window's repaint was not enough this step: every view once
                    j.Kicked = true;
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }

                if (now - j.StepAt > 1.5)
                {
                    Record(j, j.Index, false);
                    j.Timeouts++;
                    if (j.Timeouts >= 8 && j.Timeouts > j.Index / 2)
                    {
                        // the data never comes: keep the rest of the list without it
                        for (var i = j.Index + 1; i < j.Count; i++) Record(j, i, false);
                        j.Note = (j.Note == null ? "" : j.Note + "; ") + "event data timed out " + j.Timeouts + " times: the rest listed without data";
                        End(j);
                        return;
                    }

                    Next(j, now);
                    return;
                }

                Repaint();
            }
            catch (Exception ex)
            {
                j.Note = (j.Note == null ? "" : j.Note + "; ") + "step " + j.Index + ": " + Inner(ex);
                End(j);
            }
        }

        static void Next(Job j, double now)
        {
            j.Index++;
            j.StepAt = now;
            j.Kicked = false;
            if (j.Index < j.Count)
            {
                SetProp("limit", j.Index + 1);
                Repaint();
            }
        }

        static object Field(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }

        static string[] _causes;

        static string Cause(int i)
        {
            try
            {
                if (_causes == null) _causes = Call("GetBatchBreakCauseStrings") as string[];
            }
            catch
            {
                _causes = new string[0];
            }

            return _causes != null && i >= 0 && i < _causes.Length ? _causes[i] : "#" + i;
        }

        static void Bump(Dictionary<string, int> d, string k) => d[k ?? "?"] = (d.TryGetValue(k ?? "?", out var n) ? n : 0) + 1;

        static readonly HashSet<string> NonDraw = new()
        {
            "ClearNone", "ClearColor", "ClearDepth", "ClearColorDepth", "ClearStencil", "ClearColorStencil", "ClearDepthStencil",
            "ClearAll", "SetRenderTarget", "ResolveRT", "ResolveDepth", "GrabIntoRT", "ComputeDispatch", "RayTracingDispatch",
            "PluginEvent", "BeginSubpass", "HierarchyLevelBreak", "ConfigureFoveatedRendering", "SetShadingRateImage"
        };

        static void Record(Job j, int i, bool withData)
        {
            var I = CultureInfo.InvariantCulture;
            var ev = j.Events != null && i < j.Events.Length ? j.Events.GetValue(i) : null;
            var type = Field(ev, "m_Type");
            var tname = type != null ? type.ToString() : "?";
            var obj = Field(ev, "m_Obj") as UnityEngine.Object;
            string name = null;
            try { name = Call("GetFrameEventInfoName", i) as string; }
            catch { name = null; }

            var draw = !NonDraw.Contains(tname);
            Bump(j.Types, tname);
            var sb = new StringBuilder("{\"i\": ").Append(i).Append(", \"type\": ").Append(Q(tname));
            if (!string.IsNullOrEmpty(name)) sb.Append(", \"name\": ").Append(Q(name));
            if (obj != null) sb.Append(", \"obj\": ").Append(Q(obj.GetType().Name + ":" + obj.name));
            if (withData)
            {
                var d = j.Data;
                var shader = Field(d, "m_RealShaderName") as string;
                var orig = Field(d, "m_OriginalShaderName") as string;
                var pass = Field(d, "m_PassName") as string;
                var cause = Field(d, "m_BatchBreakCause") is int c ? c : -1;
                var vc = Field(d, "m_VertexCount") is int v ? v : 0;
                var ic = Field(d, "m_IndexCount") is int ix ? ix : 0;
                var dc = Field(d, "m_DrawCallCount") is int dd ? dd : 0;
                var rt = Field(d, "m_RenderTargetName") as string;
                var sref = Field(d, "m_StencilRef") is int sr ? sr : 0;
                var mesh = Field(d, "m_Mesh") as Mesh;
                string tex = null, kw = null;
                var info = Field(d, "m_ShaderInfo");
                if (Field(info, "m_Textures") is Array texs)
                    foreach (var t in texs)
                    {
                        var tn = Field(t, "m_Name") as string;
                        if (tn != "_MainTex" && tex != null) continue;
                        var tv = Field(t, "m_Value") as Texture;
                        var tt = Field(t, "m_TextureName") as string;
                        tex = tv != null ? tv.name + " " + tv.width + "x" + tv.height : tt;
                        if (tn == "_MainTex") break;
                    }

                if (Field(info, "m_Keywords") is Array kws)
                {
                    var names = new List<string>();
                    foreach (var k in kws)
                        if (Field(k, "m_Name") is string kn && !string.IsNullOrEmpty(kn))
                            names.Add(kn);
                    if (names.Count > 0) kw = string.Join(" ", names);
                }

                sb.Append(", \"shader\": ").Append(Q(shader));
                if (!string.IsNullOrEmpty(orig) && orig != shader) sb.Append(", \"orig\": ").Append(Q(orig));
                if (!string.IsNullOrEmpty(pass)) sb.Append(", \"pass\": ").Append(Q(pass));
                sb.Append(", \"tex\": ").Append(Q(tex)).Append(", \"cause\": ").Append(Q(cause >= 0 ? Cause(cause) : null))
                    .Append(", \"verts\": ").Append(vc.ToString(I)).Append(", \"indices\": ").Append(ic.ToString(I))
                    .Append(", \"drawCalls\": ").Append(dc.ToString(I));
                if (!string.IsNullOrEmpty(rt)) sb.Append(", \"rt\": ").Append(Q(rt));
                if (sref != 0) sb.Append(", \"stencilRef\": ").Append(sref.ToString(I));
                if (mesh != null) sb.Append(", \"mesh\": ").Append(Q(mesh.name));
                if (kw != null) sb.Append(", \"keywords\": ").Append(Q(kw));
                if (draw)
                {
                    j.Draws++;
                    j.DrawCalls += Math.Max(1, dc);
                    j.Verts += vc;
                    Bump(j.Causes, cause >= 0 ? Cause(cause) : "(none)");
                    Bump(j.Textures, tex ?? "(none)");
                    Bump(j.Shaders, shader ?? "(none)");
                }
            }
            else
            {
                sb.Append(", \"data\": false");
                if (draw) j.Draws++;
            }

            sb.Append('}');
            j.Rows.Add(sb.ToString());
        }

        static void End(Job j)
        {
            try
            {
                if (j.Enabled)
                {
                    try { SetProp("limit", Math.Max(1, Count)); }
                    catch { /* restored by the disable */ }

                    Call("SetEnabled", false, RemoteId());
                    j.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                j.Note = (j.Note == null ? "" : j.Note + "; ") + "disable: " + Inner(ex);
            }

            try
            {
                if (j.PausedByUs && EditorApplication.isPlaying) EditorApplication.isPaused = false;
                j.PausedByUs = false;
            }
            catch (Exception ex)
            {
                j.Note = (j.Note == null ? "" : j.Note + "; ") + "unpause: " + Inner(ex);
            }

            j.Json = Summary(j);
            j.Done = true;
            if (_job == j) _job = null;
            EditorApplication.update -= Tick;
        }

        static string Summary(Job j)
        {
            var I = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{\"label\": ").Append(Q(j.Label)).Append(", \"events\": ").Append(j.Count.ToString(I))
                .Append(", \"read\": ").Append(j.Rows.Count.ToString(I)).Append(", \"draws\": ").Append(j.Draws.ToString(I))
                .Append(", \"drawCalls\": ").Append(j.DrawCalls.ToString(I)).Append(", \"verts\": ").Append(j.Verts.ToString(I))
                .Append(", \"seconds\": ").Append((EditorApplication.timeSinceStartup - j.Started).ToString("0.0", I));
            if (j.Note != null) sb.Append(", \"note\": ").Append(Q(j.Note));
            sb.Append(", \"types\": ").Append(Map(j.Types, 99)).Append(", \"causes\": ").Append(Map(j.Causes, 99))
                .Append(", \"shaders\": ").Append(Map(j.Shaders, 40)).Append(", \"textures\": ").Append(Map(j.Textures, 80))
                .Append(", \"list\": [");
            for (var i = 0; i < j.Rows.Count; i++) sb.Append(i == 0 ? "\n      " : ",\n      ").Append(j.Rows[i]);
            sb.Append("]}");
            return sb.ToString();
        }

        static string Map(Dictionary<string, int> d, int max)
        {
            var l = new List<KeyValuePair<string, int>>(d);
            l.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            var sb = new StringBuilder("{");
            for (var i = 0; i < l.Count && i < max; i++) sb.Append(i == 0 ? "" : ", ").Append(Q(l[i].Key)).Append(": ").Append(l[i].Value);
            if (l.Count > max) sb.Append(", \"(more)\": ").Append(l.Count - max);
            return sb.Append('}').ToString();
        }

        static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var ch in s)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }

            return sb.Append('"').ToString();
        }
    }
}
