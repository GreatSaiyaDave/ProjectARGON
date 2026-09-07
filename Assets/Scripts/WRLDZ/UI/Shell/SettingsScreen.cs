using UnityEngine;
using UnityEngine.UI;
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
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/bg_auth.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/bg_menu_void.png");
            StreamingSprite.ClearCache("WRLDZ/Imagine/ui/panel_boot_glass.png");

            DualMenuPresenter.Frame frame = default;
            void Close()
            {
                DualMenuPresenter.Dismiss(frame);
                onClose?.Invoke();
            }

            frame = DualMenuPresenter.BuildFrame(
                parent, "SETTINGS", "Menu size · device · audio", Close);
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
                    well.color = new Color(0.03f, 0.05f, 0.09f, 0.28f + MenuChromePrefs.PanelAlpha * 0.40f);
            });

            HubChrome.ListHead(list, "DEVICE");
            Toggle(list, "AR Quality");
            Toggle(list, "Battery Saver", on => shell?.SetBatterySaver(on));
            Toggle(list, "Eye Tracking");
            Toggle(list, "Right-Arm Disk");
            Toggle(list, "High Contrast");

            HubChrome.ListHead(list, "AUDIO");
            HubChrome.ListRow(list, "Master Volume", null);

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

        static void Toggle(Transform list, string label, System.Action<bool> onChange = null)
        {
            var on = false;
            Button btn = null;
            btn = HubChrome.ListRow(list, label, () =>
            {
                on = !on;
                var t = btn.transform.Find("Title")?.GetComponent<Text>();
                if (t != null)
                    t.color = on ? DuelystUi.Cyan : DuelystUi.TextCream;
                HubChrome.LiftPlate(btn.GetComponent<Image>(),
                    on ? DuelystUi.Gold : DuelystUi.Cyan);
                onChange?.Invoke(on);
            });
        }
    }
}
