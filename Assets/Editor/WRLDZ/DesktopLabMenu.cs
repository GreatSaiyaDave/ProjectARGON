using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WRLDZ.UI;

namespace WRLDZ.EditorTools
{
    /// <summary>Equipment-free Desktop Lab entry points.</summary>
    public static class DesktopLabMenu
    {
        [MenuItem("WRLDZ/Lab/Open Desktop Lab App")]
        public static void OpenLab()
        {
            if (!EditorApplication.isPlaying)
            {
                if (!EditorUtility.DisplayDialog(
                        "Desktop Lab",
                        "Enter Play Mode on the Boot scene and open the Desktop Lab?\n\n" +
                        "No phone, Quest, or GPS required.",
                        "Play Boot + Lab", "Cancel"))
                    return;

                var boot = "Assets/Scenes/Boot.unity";
                if (System.IO.File.Exists(
                        System.IO.Path.Combine(Application.dataPath, "Scenes/Boot.unity")))
                {
                    EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                    EditorSceneManager.OpenScene(boot);
                }

                EditorApplication.isPlaying = true;
                // BootFlowBootstrap auto-launches DesktopLab in Editor
                return;
            }

            DesktopLabApp.Launch();
        }

        [MenuItem("WRLDZ/Lab/Clear Skip Pre-Duel Pref (force cinematic)")]
        public static void ClearSkipPref()
        {
            DesktopLabApp.SkipPreDuel = false;
            PlayerPrefs.SetInt(DesktopLabApp.PrefSkipPreDuel, 0);
            PlayerPrefs.Save();
            Debug.Log("[WRLDZ Lab] Skip pre-duel PlayerPrefs cleared — cinematic will run");
            EditorUtility.DisplayDialog(
                "Pre-Duel Cinematic",
                "Sticky skip pref cleared.\n\n" +
                "Use Desktop Lab → QUICK AR DUEL for shuffle + draw.\n" +
                "Use INSTANT DUEL only when you want hands already drawn.",
                "OK");
        }

        [MenuItem("WRLDZ/Lab/Open Desktop Lab Notes")]
        public static void OpenNotes()
        {
            var path = "Assets/Scripts/WRLDZ/DESKTOP_LAB.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            Debug.Log("[WRLDZ] Desktop Lab notes: " + path);
        }
    }
}
