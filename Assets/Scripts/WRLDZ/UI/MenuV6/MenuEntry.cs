using UnityEngine;
using UnityEngine.SceneManagement;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// How the main menu is being entered (BUILD_PLAN D8, D10, §6 P2 steps 5-6): from the title's TOUCH (the boot handoff:
    /// the menu builds in its <c>#enter</c> mode over the kept title canvas), or anything else (the first-time path's world
    /// load, a duel's return, the Desktop Lab SYSTEMS HUB: the cold open). A one-shot token set before
    /// <c>AppSession.GoMainMenu()</c> and taken by MainMenuBootstrap; an edit-mode build (MenuSmokeTest) takes none and the
    /// Stone is built at rest.
    /// </summary>
    public static class MenuEntry
    {
        public enum Mode
        {
            None,
            ColdOpen,
            FromTitle
        }

        static Mode _pending = Mode.ColdOpen;

        /// <summary>The kept title canvas of the last handoff (null once it has gone).</summary>
        public static TitleKeepAlive Title { get; private set; }

        public static Mode Pending => _pending;

        /// <summary>The menu's next build takes the boot handoff over the kept title canvas.</summary>
        public static void SetFromTitle(TitleKeepAlive kept)
        {
            _pending = Mode.FromTitle;
            Title = kept;
            TakenFrame = -1;
        }

        public static void SetColdOpen() => _pending = Mode.ColdOpen;

        /// <summary>The frame the menu took the token in (MainMenuBootstrap builds the menu in that frame); -1 = not yet since
        /// the last handoff. The kept title's u clock and the menu's clock start on the frame after it (D8: Time.unscaledTime is
        /// a frame's start, so a long build frame must not be counted as entrance time).</summary>
        public static int TakenFrame { get; private set; } = -1;

        /// <summary>Take the token (MainMenuBootstrap): the next entry without a token is a cold open again.</summary>
        public static Mode Take()
        {
            var m = _pending;
            _pending = Mode.ColdOpen;
            TakenFrame = Time.frameCount;
            if (m == Mode.FromTitle && (Title == null || !Title)) m = Mode.ColdOpen;   // the title canvas is already gone
            return m;
        }

        /// <summary>
        /// D8, on TOUCH for a returning player: the title canvas (a root object) is kept alive across the scene load —
        /// DontDestroyOnLoad, raycasts off, sort 49 under the menu's 50 — and the token is set. The caller then calls
        /// <c>AppSession.GoMainMenu()</c> as before (its audio switch included). The kept canvas animates its Tears and
        /// sky and destroys itself at u = 1.5 (or on the next scene change, or after 4 s).
        /// </summary>
        public static TitleKeepAlive KeepTitle(GameObject bootCanvas, TitleStage title)
        {
            if (bootCanvas == null) return null;
            if (bootCanvas.transform.parent != null) bootCanvas.transform.SetParent(null, true);
            Object.DontDestroyOnLoad(bootCanvas);
            var cg = bootCanvas.GetComponent<CanvasGroup>();
            if (cg == null) cg = bootCanvas.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            var c = bootCanvas.GetComponent<Canvas>();
            if (c != null) c.sortingOrder = 49;
            var k = bootCanvas.GetComponent<TitleKeepAlive>();
            if (k == null) k = bootCanvas.AddComponent<TitleKeepAlive>();
            k.Title = title;
            SetFromTitle(k);
            return k;
        }

        internal static void Gone(TitleKeepAlive k)
        {
            if (Title == k) Title = null;
        }
    }

    /// <summary>
    /// The kept title canvas (D8): its u clock starts on the first frame AFTER the frame the menu was built in
    /// (<see cref="MenuEntry.TakenFrame"/>; the menu's own clock starts on that frame too, MenuV6Clock.Restart, and the
    /// title's clock is held across the build frame, TitleStage), so a build freeze never skips the TOUCH fade; it fades the
    /// title's touch / credit lines and Tears (TitleStage.Leave), and it destroys the canvas at u = 1.5. Backstops (§11 F18):
    /// the next scene change that is not the menu, or 4 s of real time without a menu build.
    /// </summary>
    public sealed class TitleKeepAlive : MonoBehaviour
    {
        public const float DestroyAtU = 1.5f, Backstop = 4f;
        public TitleStage Title;
        float _born = float.NaN, _u0 = float.NaN;
        bool _menuSeen;

        public float U => float.IsNaN(_u0) ? 0f : Time.unscaledTime - _u0;

        void OnEnable()
        {
            SceneManager.activeSceneChanged += OnScene;
            _born = Time.unscaledTime;
        }

        void OnDisable() => SceneManager.activeSceneChanged -= OnScene;

        void OnScene(Scene from, Scene to)
        {
            if (to.name == WRLDZ.Core.AppSession.SceneMainMenu)
            {
                _menuSeen = true;
                return;
            }

            if (_menuSeen || to.name != WRLDZ.Core.AppSession.SceneBoot) Kill();
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (float.IsNaN(_born)) _born = Time.unscaledTime;
            var taken = MenuEntry.TakenFrame;
            if (float.IsNaN(_u0) && taken >= 0 && Time.frameCount > taken)
            {
                _u0 = Time.unscaledTime;
                if (Title != null) Title.RestartLeaveClock();
            }

            if (!float.IsNaN(_u0))
            {
                if (U >= DestroyAtU) Kill();
            }
            else if (Time.unscaledTime - _born > Backstop) Kill();
        }

        void Kill()
        {
            MenuEntry.Gone(this);
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        void OnDestroy() => MenuEntry.Gone(this);
    }
}
