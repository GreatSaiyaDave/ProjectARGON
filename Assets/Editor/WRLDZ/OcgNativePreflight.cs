using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WRLDZ.Duel.Ocg;

namespace WRLDZ.EditorTools
{
    public static class OcgNativePreflight
    {
        public const string ResultRelative = "Library/OcgNativePreflight.last.json";

        [MenuItem("WRLDZ/Lab/Run OCG Native Preflight")]
        public static void RunInteractive()
        {
            var r = Run();
            EditorUtility.DisplayDialog(r.Ok ? "OCG Preflight PASS" : "OCG Preflight FAIL", r.Summary, "OK");
        }

        public static void RunBatch()
        {
            var r = Run();
            Debug.Log(r.Ok ? "[WRLDZ OCG PREFLIGHT] PASS\n" + r.Summary : "[WRLDZ OCG PREFLIGHT] FAIL\n" + r.Summary);
            EditorApplication.Exit(r.Ok ? 0 : 1);
        }

        public struct Report
        {
            public bool Ok;
            public string Summary;
        }

        public static Report Run()
        {
            var fp = OcgPreflightState.ComputeFingerprint(out var major, out var minor);
            try
            {
                if (!OcgNative.TryLoad(out var err))
                    return Fail(fp, major, minor, err);

                var player = OcgLabDecks.ExpandMain("ocg_lab_player.json");
                var ai = OcgLabDecks.ExpandMain("ocg_lab_ai.json");
                var pEx = OcgLabDecks.ExpandExtra("ocg_lab_player.json");
                var aEx = OcgLabDecks.ExpandExtra("ocg_lab_ai.json");
                if (player.Length != 40 || ai.Length != 40)
                    return Fail(fp, major, minor, "deck sizes " + player.Length + "/" + ai.Length + " (need 40/40)");
                if (pEx.Length < 1 || aEx.Length < 1)
                    return Fail(fp, major, minor, "extra sizes " + pEx.Length + "/" + aEx.Length);
                if (player.Length + ai.Length <= 2)
                    return Fail(fp, major, minor, "refusing two-card duel");

                using (var core = new NativeOcgDuelCore())
                {
                    core.CreateDuel(1, new OcgDuelStartInfo
                    {
                        Seed = new uint[] { 1, 0, 0, 0 },
                        PlayerMain = player,
                        OpponentMain = ai,
                        PlayerExtra = pEx,
                        OpponentExtra = aEx
                    });
                    var want = player.Length + ai.Length + pEx.Length + aEx.Length;
                    if (core.CardsRegistered != want)
                        return Fail(fp, major, minor, "registered " + core.CardsRegistered + " want " + want);

                    var idle = false;
                    for (var i = 0; i < 64; i++)
                    {
                        var msgs = core.Process();
                        foreach (var m in msgs)
                        {
                            if (m.MsgId == OcgMessageIds.Retry)
                                return Fail(fp, major, minor, "MSG_RETRY");
                            if (m.MsgId == OcgMessageIds.SelectIdleCmd)
                                idle = true;
                        }
                        if (idle) break;
                        if (core.Status == OcgDuelStatus.End)
                            break;
                        if (core.IsWaiting)
                            break;
                    }
                    if (!idle)
                        return Fail(fp, major, minor, "no MSG_SELECT_IDLECMD status=" + core.Status);
                }

                Write(new OcgPreflightState.ResultFile
                {
                    ok = true,
                    major = major,
                    minor = minor,
                    idleSeen = true,
                    preflightFingerprint = fp,
                    reason = ""
                });
                return new Report { Ok = true, Summary = "idleSeen fingerprint=" + fp };
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return Fail(fp, major, minor, ex.Message);
            }
        }

        public static bool IsCurrentNativeOk() => OcgPreflightState.TryUseNative(out _);

        static Report Fail(string fp, int major, int minor, string reason)
        {
            Write(new OcgPreflightState.ResultFile
            {
                ok = false,
                major = major,
                minor = minor,
                idleSeen = false,
                preflightFingerprint = fp,
                reason = reason ?? ""
            });
            return new Report { Ok = false, Summary = reason };
        }

        static void Write(OcgPreflightState.ResultFile r)
        {
            var path = OcgPreflightState.ResultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(r, true));
        }
    }
}
