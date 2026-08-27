using UnityEditor;
using UnityEngine;
using WRLDZ.Presentation;

namespace WRLDZ.EditorTools
{
    /// <summary>Salvage optical-see-through combiner lab.</summary>
    public static class SalvageLensesMenu
    {
        [MenuItem("WRLDZ/Lab/Salvage Lenses (optical see-through)")]
        public static void Toggle()
        {
            var next = !ArSalvageLensesSession.PreferSalvageOst;
            ArSalvageLensesSession.PreferSalvageOst = next;
            if (next)
                ArPresentationTarget.SetOverride(ArPresentationMode.SalvageOst);
            else
                ArPresentationTarget.SetOverride(null);

            Debug.Log("[WRLDZ Salvage] PreferSalvageOst=" + next + " · " +
                      ArPresentationTarget.StatusLabel());
            EditorUtility.DisplayDialog(
                "Salvage OST",
                next
                    ? "Optical see-through ON.\n\n" +
                      "Play a duel: black background + holos, gyro / RMB look, " +
                      "letterboxed optical window.\n\n" +
                      "[ ] size the window to match your combiner. P saves. R recenters.\n\n" +
                      "See Assets/Scripts/WRLDZ/SALVAGE_LENSES.md"
                    : "Salvage OST off. Phone AR / Editor sim restored.",
                "OK");
        }

        [MenuItem("WRLDZ/Lab/Salvage Lenses (optical see-through)", true)]
        public static bool ToggleValidate()
        {
            Menu.SetChecked("WRLDZ/Lab/Salvage Lenses (optical see-through)",
                ArSalvageLensesSession.PreferSalvageOst);
            return true;
        }

        [MenuItem("WRLDZ/Lab/Open Salvage Lenses Notes")]
        public static void OpenNotes()
        {
            var path = "Assets/Scripts/WRLDZ/SALVAGE_LENSES.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            EditorUtility.DisplayDialog(
                "Salvage AR lenses",
                "Keep the S23. Hunt AMOLED donors, Gear VR lenses, combiner glass.\n" +
                "Cardboard bench tonight — 50 mm lens, 45° splitter, phone screen-down.\n\n" +
                path,
                "OK");
        }
    }
}
