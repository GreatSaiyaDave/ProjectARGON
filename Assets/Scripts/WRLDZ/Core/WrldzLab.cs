using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif
using WRLDZ.Presentation;

namespace WRLDZ.Core
{
    /// <summary>
    /// Lab configuration for the real test rig:
    /// <b>Galaxy S23 Ultra + current PC</b> — no headset required.
    /// Call <see cref="Apply"/> from every scene bootstrap.
    /// </summary>
    public static class WrldzLab
    {
        public const string ProductName = "Duel Monsters WRLDZ";
        public const string LabDevice = "Galaxy S23 Ultra";
        public const int TargetFpsMobile = 60;
        public const int TargetFpsEditor = 60;

        /// <summary>Portrait phone reference (S23 Ultra class).</summary>
        public static readonly Vector2Int PortraitRef = new(1080, 2340);

        static bool _applied;
        static bool _permissionsRequested;

        /// <summary>
        /// Portrait lock, FPS, presentation logging. Safe to call multiple times.
        /// </summary>
        public static void Apply()
        {
            Application.targetFrameRate = Application.isMobilePlatform
                ? TargetFpsMobile
                : TargetFpsEditor;
            QualitySettings.vSyncCount = 0;

            if (ArPresentationTarget.IsSalvageOst)
                ApplyWearableLandscape();
            else
                ApplyPortraitLock();

            // Never force LensesXr in lab — phone/Editor only until HMD exists
            if (ArPresentationTarget.IsLenses && !Application.isEditor &&
                !ArPresentationTarget.IsSalvageOst)
            {
                // Soft: only if XR falsely reported; override to phone when mobile
                if (Application.isMobilePlatform)
                    ArPresentationTarget.SetOverride(ArPresentationMode.PhoneCamera);
            }

            if (!_applied)
            {
                _applied = true;
                Debug.Log(
                    $"[WRLDZ Lab] {ProductName} · rig={LabDevice}+PC · " +
                    $"mode={ArPresentationTarget.Current} · {ArPresentationTarget.StatusLabel()} · " +
                    $"platform={Application.platform} · {Screen.width}x{Screen.height}");
            }
        }

        /// <summary>
        /// Request CAMERA + LOCATION on Android (S23). Run as coroutine from a MonoBehaviour.
        /// Editor / desktop: no-op success.
        /// </summary>
        public static IEnumerator RequestLabPermissions()
        {
            if (_permissionsRequested)
                yield break;
            _permissionsRequested = true;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Debug.Log("[WRLDZ Lab] Requesting CAMERA…");
                Permission.RequestUserPermission(Permission.Camera);
                var t = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && t < 12f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // Android 12+: request fine + coarse (GO-style overworld needs outdoor fix)
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Debug.Log("[WRLDZ Lab] Requesting LOCATION (fine)…");
                Permission.RequestUserPermission(Permission.FineLocation);
                var t = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && t < 10f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
            {
                Debug.Log("[WRLDZ Lab] Requesting LOCATION (coarse)…");
                Permission.RequestUserPermission(Permission.CoarseLocation);
                var t = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation) && t < 6f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            Debug.Log(
                $"[WRLDZ Lab] permissions camera={Permission.HasUserAuthorizedPermission(Permission.Camera)} " +
                $"fineLoc={Permission.HasUserAuthorizedPermission(Permission.FineLocation)} " +
                $"coarseLoc={Permission.HasUserAuthorizedPermission(Permission.CoarseLocation)}");
#else
            // Desktop / iOS path: Unity webcam authorization (Editor may use host camera)
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.Log("[WRLDZ Lab] Requesting WebCam authorization…");
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }
#endif
            yield return null;
        }

        public static bool HasCameraPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
                return Application.HasUserAuthorization(UserAuthorization.WebCam)
                       || Application.isEditor;
#endif
            }
        }

        /// <summary>True if the OS granted fine or coarse location (map GPS).</summary>
        public static bool HasLocationPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(Permission.FineLocation)
                       || Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);
#else
                // Editor / desktop: walk pad + WASD simulate GPS meters
                return true;
#endif
            }
        }

        public static bool IsPhoneLab =>
            Application.isMobilePlatform ||
            (Application.isEditor && ArPresentationTarget.PreferEditorCamera);

        /// <summary>Map / digital / handheld AR — portrait lock.</summary>
        public static void ApplyPortraitLock()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
        }

        /// <summary>Salvage combiner bench / visor — landscape while the session is up.</summary>
        public static void ApplyWearableLandscape()
        {
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }

        public static string ModeBanner() =>
            Application.isMobilePlatform
                ? "S23 · PHONE AR"
                : Application.isEditor
                    ? "PC EDITOR · SIM"
                    : ArPresentationTarget.StatusLabel();
    }
}
