using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// Human-visible proof the owner's Unity Hub folder is current.
    /// Bump Stamp when shipping overlay/engine drops they need to re-Add.
    /// </summary>
    public static class WrldzBuild
    {
        public const string Stamp = "PLATES-0907";
        public const string Line =
            "[WRLDZ] BUILD " + Stamp + " · plated overlays · if missing, Hub opened an old folder";

        public static void Log() => Debug.Log(Line);
    }
}
