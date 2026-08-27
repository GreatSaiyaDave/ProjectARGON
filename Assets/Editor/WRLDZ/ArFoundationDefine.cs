using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Adds WRLDZ_HAS_ARFOUNDATION when com.unity.xr.arfoundation is actually loaded.
    /// Scripts compile as stubs until then (Unity will not resolve packages in Play Mode).
    /// </summary>
    [InitializeOnLoad]
    public static class ArFoundationDefine
    {
        const string Symbol = "WRLDZ_HAS_ARFOUNDATION";

        static ArFoundationDefine()
        {
            EditorApplication.delayCall += Sync;
        }

        static void Sync()
        {
            var has = System.Type.GetType(
                          "UnityEngine.XR.ARFoundation.ARSession, Unity.XR.ARFoundation") != null;
            foreach (var named in new[]
                     {
                         NamedBuildTarget.Standalone,
                         NamedBuildTarget.Android,
                         NamedBuildTarget.iOS
                     })
            {
                var raw = PlayerSettings.GetScriptingDefineSymbols(named) ?? "";
                var list = raw.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                var changed = false;
                if (has && !list.Contains(Symbol))
                {
                    list.Add(Symbol);
                    changed = true;
                }
                else if (!has && list.Contains(Symbol))
                {
                    list.Remove(Symbol);
                    changed = true;
                }

                if (changed)
                {
                    PlayerSettings.SetScriptingDefineSymbols(named, string.Join(";", list));
                    Debug.Log($"[WRLDZ ARCore] {Symbol}={(has ? "ON" : "OFF")} on {named.TargetName}");
                }
            }
        }
    }
}
