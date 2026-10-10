using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Settings sheet — same Battle City capsule chrome as hub overlays.
    /// Menu size & transparency prefs apply live to deck + systems menus (AR + phone).
    /// </summary>
    public static class SettingsScreen
    {
        public static RectTransform Build(Transform parent, MenuShell shell, System.Action onClose)
        {
            FreeUiKit.EnsureLoaded();
#if UNITY_EDITOR
            // Editor art hot-reload only.
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/bg_auth.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/bg_menu_void.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/ui/panel_boot_glass.png");
#endif

            DualMenuPresenter.Frame frame = default;
            void Close()
            {
                DualMenuPresenter.Dismiss(frame);
                onClose?.Invoke();
            }

            frame = DualMenuPresenter.BuildFrame(
                parent, "SETTINGS", "Menu size · device", Close);
            var body = frame.BodyHost;
            var well = frame.Root != null ? frame.Root.Find("BodyWell")?.GetComponent<Image>() : null;

            var scroll = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            scroll.transform.SetParent(body, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.98f, 0.98f);
            var v = scroll.GetComponent<VerticalLayoutGroup>();
            v.spacing = 8;
            v.childForceExpandHeight = false;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.padding = new RectOffset(4, 4, 4, 4);
            scroll.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var list = scroll.transform;

            HubChrome.ListHead(list, "MENU");
            CycleRow(list, () => "Size · " + MenuChromePrefs.SizeLabel, () => MenuChromePrefs.CycleSize());
            CycleRow(list, () => "Transparency · " + MenuChromePrefs.OpacityLabel, () =>
            {
                MenuChromePrefs.CycleOpacity();
                if (well != null)
                    well.color = MenuChromePrefs.InsetColor;
            });

            // Only rows that do something. AR Quality, Eye Tracking, Right-Arm Disk, High Contrast and
            // Master Volume were wired to nothing and are hidden until implemented (QC F12).
            HubChrome.ListHead(list, "DEVICE");
            Toggle(list, "Battery Saver", WrldzTheme.BatterySaver, on =>
            {
                if (shell != null) shell.SetBatterySaver(on);
                else WrldzTheme.BatterySaver = on;
            });

            HubChrome.ListHead(list, "DUEL");
            CycleRow(list, () => "AI Difficulty · " + AiDifficultyPrefs.Current, () =>
            {
                AiDifficultyPrefs.Cycle(AiDifficultyPrefs.Current);
            });

            return frame.Root;
        }

        static void CycleRow(Transform list, System.Func<string> labelFn, System.Action onCycle)
        {
            Button btn = null;
            btn = HubChrome.ListRow(list, labelFn(), () =>
            {
                onCycle?.Invoke();
                var t = btn.transform.Find("Title")?.GetComponent<Text>();
                if (t != null) t.text = labelFn();
            });
        }

        /// <summary>On/off row that starts from the stored value (<paramref name="initial"/>).</summary>
        static void Toggle(Transform list, string label, bool initial, System.Action<bool> onChange = null)
        {
            var on = initial;
            Button btn = null;
            void Paint()
            {
                var t = btn.transform.Find("Title")?.GetComponent<Text>();
                if (t != null)
                    t.color = on ? DuelystUi.Cyan : DuelystUi.TextCream;
                HubChrome.LiftPlate(btn.GetComponent<Image>(),
                    on ? DuelystUi.Gold : DuelystUi.Cyan);
            }

            btn = HubChrome.ListRow(list, label, () =>
            {
                on = !on;
                Paint();
                onChange?.Invoke(on);
            });
            if (on) Paint();
        }
    }
}
