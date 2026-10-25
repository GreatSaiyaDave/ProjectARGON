using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    public enum MenuLayoutKind
    {
        Landscape,
        Portrait
    }

    /// <summary>
    /// D1: which of the two layout plans a menu canvas uses. Landscape = the 1920×1080 reference (match 0.5) the owner
    /// judges in; portrait = today's 1080×2340 phone plan. The surface is the real screen in Play mode, a forced size
    /// for captures / tests, and landscape in edit mode (§11 F14: <c>Screen.width/height</c> in edit mode reports
    /// whichever Editor view is drawing, not the Game view). The orientation lock (AppSession) is untouched, so phones
    /// stay portrait.
    /// </summary>
    public static class MenuAspect
    {
        /// <summary>The mockup stage (layout_land.json stage W×H); 1 stage px = 1 canvas unit at the reference.</summary>
        public static readonly Vector2 LandStage = new(1920f, 1080f);

        static bool _forced;
        static Vector2 _forcedSize;

        /// <summary>Use this surface instead of the screen (MenuV6Capture, MenuSmokeTest). Pass 0, 0 to clear.</summary>
        public static void Force(int w, int h)
        {
            _forced = w > 0 && h > 0;
            _forcedSize = new Vector2(w, h);
        }

        public static void ClearForce() => _forced = false;
        public static bool IsForced => _forced;

        /// <summary>The surface in pixels the canvases are laid out for.</summary>
        public static Vector2 Surface
        {
            get
            {
                if (_forced) return _forcedSize;
                if (!Application.isPlaying) return LandStage;
                return new Vector2(Screen.width, Screen.height);
            }
        }

        public static bool IsLandscape => UiTheme.IsLandscapeSurface(Surface);

        public static MenuLayoutKind Current => IsLandscape ? MenuLayoutKind.Landscape : MenuLayoutKind.Portrait;

        /// <summary>
        /// Canvas units per mockup stage px for a canvas root (the uniform "contain" fit of the stage into the canvas).
        /// 1 at a 16:9 surface with the landscape scaler.
        /// </summary>
        public static float StageScale(RectTransform canvasRoot, Vector2 stage)
        {
            if (canvasRoot == null || stage.x <= 0f || stage.y <= 0f) return 1f;
            var r = canvasRoot.rect;
            if (r.width <= 0f || r.height <= 0f) return 1f;
            return Mathf.Min(r.width / stage.x, r.height / stage.y);
        }
    }

    /// <summary>
    /// P4 (§11 F3): keeps an aspect-plan canvas's scaler on the plan of the current surface (MenuShellOverlay): a Game view
    /// turned from landscape to portrait (or a capture's forced size) re-applies <see cref="UiTheme.ApplyForAspect"/>; the
    /// sheets re-anchor, nothing is rebuilt. Edit mode keeps what the canvas was built with.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuAspectScaler : MonoBehaviour
    {
        CanvasScaler _scaler;
        bool _land;

        public static MenuAspectScaler Attach(RectTransform canvas)
        {
            if (canvas == null) return null;
            if (!canvas.TryGetComponent(out MenuAspectScaler f)) f = canvas.gameObject.AddComponent<MenuAspectScaler>();
            f._scaler = canvas.GetComponent<CanvasScaler>();
            f._land = MenuAspect.IsLandscape;
            return f;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || _scaler == null) return;
            var land = MenuAspect.IsLandscape;
            if (land == _land) return;
            _land = land;
            UiTheme.ApplyForAspect(_scaler, MenuAspect.Surface);
        }
    }
}
