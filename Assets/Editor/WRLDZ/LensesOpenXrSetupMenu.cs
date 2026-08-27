using UnityEditor;
using UnityEngine;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Quest 3 / OpenXR setup helpers for life-size lenses duels.
    /// </summary>
    public static class LensesOpenXrSetupMenu
    {
        [MenuItem("WRLDZ/Lab/Open Lenses XR Setup Notes")]
        public static void OpenNotes()
        {
            var path = "Assets/Scripts/WRLDZ/LENSES_XR_SETUP.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            Debug.Log("[WRLDZ] Lenses setup: " + System.IO.Path.GetFullPath(path));
            EditorUtility.DisplayDialog(
                "Lenses XR (Quest 3)",
                "1. Project Settings → XR Plug-in Management → Android: OpenXR\n" +
                "2. OpenXR → Meta Quest Support + Oculus Touch (+ Touch Plus)\n" +
                "3. Build Settings → Android · ARM64 · Quest\n" +
                "4. Deploy APK · put on headset · start duel\n" +
                "5. Left controller = your disk · arena on floor @ 1:1 m\n\n" +
                "See Assets/Scripts/WRLDZ/LENSES_XR_SETUP.md",
                "OK");
        }

        [MenuItem("WRLDZ/Lab/Log Lenses Session Status")]
        public static void LogStatus()
        {
            var running = WRLDZ.Presentation.ArLensesSession.IsXrDisplayRunning();
            Debug.Log(
                $"[WRLDZ Lenses] XR display running={running} · " +
                $"mode={WRLDZ.Presentation.ArPresentationTarget.Current} · " +
                WRLDZ.Presentation.ArPresentationTarget.StatusLabel());
        }
    }
}
