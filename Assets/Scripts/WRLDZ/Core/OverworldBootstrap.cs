using UnityEngine;
using WRLDZ.UI;

namespace WRLDZ.Core
{
    /// <summary>Entry for Overworld scene — GO-style map + avatar.</summary>
    public class OverworldBootstrap : MonoBehaviour
    {
        void Start()
        {
            WrldzLab.Apply();
            StartCoroutine(Boot());
        }

        System.Collections.IEnumerator Boot()
        {
            // Location for GPS map on S23 (indoor falls back to walk pad)
            yield return WrldzLab.RequestLabPermissions();
            try
            {
                AppSession.Ensure();
                AppSession.Ensure().RefreshFromStore();
                DestroyIfExists("OverworldCanvas");
                DestroyIfExists("OverworldMapCanvas");
                DestroyIfExists("OverworldHudCanvas");
                DestroyIfExists("OverworldHost");

                var host = new GameObject("OverworldHost");
                var ui = host.AddComponent<OverworldUI>();
                ui.Build();
                Debug.Log("[WRLDZ Lab] Overworld ready — " + WrldzLab.ModeBanner());
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[WRLDZ] OverworldBootstrap failed: " + ex);
            }
        }

        static void DestroyIfExists(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Destroy(go);
        }
    }
}
