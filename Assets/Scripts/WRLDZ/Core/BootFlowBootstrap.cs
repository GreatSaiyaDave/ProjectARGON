using UnityEngine;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Core
{
    /// <summary>
    /// Entry for Boot scene only — splash → title → load → DOB? → auth → terms? → world load → Overworld.
    /// See FLOW.md for the full menu graph.
    /// Desktop Lab is opt-in (CLI <c>-desktopLab</c> or PrefSkipBootCascade) so title music / splash always show by default.
    /// </summary>
    public class BootFlowBootstrap : MonoBehaviour
    {
        void Start()
        {
            WrldzLab.Apply();
            try
            {
                // Soft UI SFX + title theme (Scarab Under Stone) for splash / title
                // Audio must never block UI — Ensure is sync; clip load is async
                try
                {
                    WrldzAudio.Ensure();
                    WrldzAudio.SetBgmEnabled(true);
                    WrldzAudio.SetBgmVolume(0.28f);
                    WrldzAudio.PlayTitleBgm();
                }
                catch (System.Exception audioEx)
                {
                    Debug.LogWarning("[WRLDZ] Audio init skipped: " + audioEx.Message);
                }

                AppSession.Ensure();
                DestroyIfExists("BootCanvas");
                DestroyIfExists("BootFlowHost");
                DestroyIfExists("DesktopLabApp");
                DestroyIfExists("DesktopLabCanvas");

                // Opt-in lab only (CLI / explicit pref) — Editor no longer skips splash
                if (DesktopLabApp.ShouldAutoLaunch())
                {
                    Debug.Log("[WRLDZ Lab] Desktop Lab (skip boot) — " + WrldzLab.ModeBanner());
                    DesktopLabApp.Launch();
                    return;
                }

                var host = new GameObject("BootFlowHost");
                var ui = host.AddComponent<BootFlowUI>();
                ui.BuildAndRun();
                Debug.Log("[WRLDZ] Boot cascade (splash → title) — " + WrldzLab.ModeBanner());
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[WRLDZ] BootFlowBootstrap failed: " + ex);
                // Last-resort: still try to show something usable
                try
                {
                    DestroyIfExists("BootFlowHost");
                    var host = new GameObject("BootFlowHost");
                    host.AddComponent<BootFlowUI>().BuildAndRun();
                }
                catch (System.Exception ex2)
                {
                    Debug.LogError("[WRLDZ] Boot recovery failed: " + ex2);
                }
            }
        }

        static void DestroyIfExists(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Destroy(go);
        }
    }
}
