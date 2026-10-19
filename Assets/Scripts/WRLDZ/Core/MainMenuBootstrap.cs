using UnityEngine;
using WRLDZ.UI;

namespace WRLDZ.Core
{
    /// <summary>
    /// Entry for the MainMenu scene: home — "The Stone Beneath Nut" v6.3 main menu (DuelDiskMenuUI). Boot lands here
    /// (the title's TOUCH, or the first-time path's world load); the Overworld is reached by WORLD MAP. The entry token
    /// (MenuEntry: the boot handoff over the kept title canvas, else the cold open) picks the menu's entrance.
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
                menu.Entry = WRLDZ.UI.MenuV6.MenuEntry.Take();
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
