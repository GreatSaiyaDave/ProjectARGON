using UnityEngine;
using WRLDZ.UI;

namespace WRLDZ.Core
{
    /// <summary>
    /// Entry for MainMenu scene — Mr. Referobot systems hub.
    /// Entered only from Overworld Disk HUB. Not home (home is Overworld).
    /// Requires login; otherwise returns to Boot.
    /// </summary>
    public class MainMenuBootstrap : MonoBehaviour
    {
        [Tooltip("Scene name of the duel vertical slice")]
        public string duelSceneName = "DuelSlice";

        void Start()
        {
            WrldzLab.Apply();
            AppSession.Ensure();
            if (!AppSession.Ensure().IsLoggedIn)
            {
                Debug.LogWarning("[WRLDZ] No session — returning to Boot.");
                AppSession.Ensure().GoBoot();
                return;
            }

            try
            {
                DestroyIfExists("DuelCanvas");
                DestroyIfExists("DiskMenuCanvas");
                DestroyIfExists("MainMenuHost");

                var host = new GameObject("MainMenuHost");
                var menu = host.AddComponent<DuelDiskMenuUI>();
                menu.duelSceneName = duelSceneName;
                menu.Build();
                Debug.Log("[WRLDZ Lab] " + DuelDiskMenuUI.GameTitle + " hub · " +
                          WrldzLab.ModeBanner() + " · " +
                          AppSession.Ensure().Account.displayName);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[WRLDZ] MainMenuBootstrap failed: " + ex);
            }
        }

        static void DestroyIfExists(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
                Destroy(go);
        }
    }
}
