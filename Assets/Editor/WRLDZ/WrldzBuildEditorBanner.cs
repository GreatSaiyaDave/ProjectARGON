using UnityEditor;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.EditorTools
{
    /// <summary>Fires when scripts compile so the owner sees BUILD without Play.</summary>
    [InitializeOnLoad]
    static class WrldzBuildEditorBanner
    {
        static WrldzBuildEditorBanner()
        {
            Debug.Log(WrldzBuild.Line);
        }
    }
}
