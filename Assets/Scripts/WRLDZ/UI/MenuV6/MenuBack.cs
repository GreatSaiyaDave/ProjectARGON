using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// Android back / Escape on the main menu (BUILD_PLAN §6 P5 step 3; §3: always WrldzInput, never the legacy key API):
    /// <see cref="WrldzInput.KeyDown"/>(Escape) — the Input System reports the Android back button as the keyboard's escape
    /// key (guess, verified on the first device run) — closes the top sheet through its own Close button (the frame's back
    /// chip: the deck builder's goes through RequestLeaveEditor, so nothing unsaved is dropped); else the popover / Stone
    /// sheet takes STAY (its own close); on the bare main menu it does nothing. One press = one step.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuBack : MonoBehaviour
    {
        /// <summary>The owner's step for one press (true = handled).</summary>
        public Func<bool> Step;

        /// <summary>Tests: what the last press did ("sheet", "popover", "nothing").</summary>
        public string Last { get; private set; } = "";

        int _frame = -1;

        public static MenuBack Attach(GameObject host, Func<bool> step)
        {
            if (host == null) return null;
            var b = host.GetComponent<MenuBack>();
            if (b == null) b = host.AddComponent<MenuBack>();
            b.Step = step;
            return b;
        }

        void Update()
        {
            if (!WrldzInput.KeyDown(KeyCode.Escape)) return;
            if (_frame == Time.frameCount) return;   // one step per press, whichever menu sees it first
            _frame = Time.frameCount;
            Press();
        }

        /// <summary>What one back press does now (tests call it directly).</summary>
        public bool Press()
        {
            var ok = false;
            try { ok = Step != null && Step(); }
            catch (Exception ex) { Debug.LogWarning("[WRLDZ MenuV6] back: " + ex.Message); }
            if (!ok) Last = "nothing";
            return ok;
        }

        internal void Mark(string what) => Last = what;

        /// <summary>The top sheet of an overlay shell: the last active, interactable Button named "Close" under its modal
        /// host (drawn last = on top); false when the shell shows no sheet.</summary>
        public static bool CloseTopSheet(MenuShell shell)
        {
            if (shell == null || shell.ModalHost == null || shell.Root == null) return false;
            if (!shell.Root.gameObject.activeInHierarchy || shell.ModalHost.childCount == 0) return false;
            Button top = null;
            foreach (var b in shell.ModalHost.GetComponentsInChildren<Button>(false))
            {
                if (b == null || b.name != "Close" || !b.IsInteractable() || !b.gameObject.activeInHierarchy) continue;
                top = b;
            }

            if (top == null) return false;
            top.onClick.Invoke();
            return true;
        }
    }
}
