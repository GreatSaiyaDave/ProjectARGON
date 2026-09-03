using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Spec §3.1 floating HUD: Level, Digizeni, Duel Coins, Set Energy, Kuriboh.
    /// High-contrast chips for outdoor map play.
    /// </summary>
    public class HudTokens : MonoBehaviour
    {
        Text _level;
        Text _digizeni;
        Text _coins;
        Text _energy;
        Text _navi;
        Image _naviFace;

        public static HudTokens Create(Transform parent)
        {
            var go = new GameObject("HudTokens", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            FloatingPanel.Place(rt, 0.02f, 0.90f, 0.98f, 0.995f);

            // Glass bar
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(go.transform, false);
            FloatingPanel.Stretch(bar.GetComponent<RectTransform>());
            var bimg = bar.GetComponent<Image>();
            bimg.sprite = ImagineAssets.BarTopHud() ?? UiFoundation.WhiteSprite();
            bimg.type = bimg.sprite != null && bimg.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            bimg.color = Color.white;
            bimg.raycastTarget = false;

            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(go.transform, false);
            FloatingPanel.Stretch(row.GetComponent<RectTransform>(), 6f);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.padding = new RectOffset(10, 10, 4, 4);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var hud = go.AddComponent<HudTokens>();
            hud._level = Chip(row.transform, "Lv", DuelystUi.Gold, ImagineAssets.LevelRing());
            hud._digizeni = Chip(row.transform, "Đ", DuelystUi.Cyan, ImagineAssets.IconDigizeni());
            hud._coins = Chip(row.transform, "◎", DuelystUi.GoldHot, ImagineAssets.IconDuelCoin());
            hud._energy = Chip(row.transform, "⚡", DuelystUi.Green, ImagineAssets.IconSetEnergy());
            hud._navi = Chip(row.transform, "Navi", DuelystUi.Magenta, ImagineAssets.NaviSpirit());
            return hud;
        }

        static Text Chip(Transform parent, string prefix, Color accent, Sprite icon = null)
        {
            var go = new GameObject("Chip_" + prefix, typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = ImagineAssets.HudChip() ?? UiFoundation.WhiteSprite();
            img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            img.color = Color.white;
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;

            if (icon != null)
            {
                var iGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iGo.transform.SetParent(go.transform, false);
                var iRt = iGo.GetComponent<RectTransform>();
                iRt.anchorMin = new Vector2(0.02f, 0.12f);
                iRt.anchorMax = new Vector2(0.28f, 0.88f);
                iRt.offsetMin = Vector2.zero;
                iRt.offsetMax = Vector2.zero;
                var iImg = iGo.GetComponent<Image>();
                iImg.sprite = icon;
                iImg.preserveAspect = true;
                iImg.raycastTarget = false;
                iImg.color = Color.white;
            }

            var tGo = new GameObject("T", typeof(RectTransform), typeof(Text));
            tGo.transform.SetParent(go.transform, false);
            var t = tGo.GetComponent<Text>();
            WrldzType.Style(t, 13, display: false, heavyOutline: true);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = accent;
            t.text = prefix;
            t.raycastTarget = false;
            var tRt = tGo.GetComponent<RectTransform>();
            if (icon != null)
            {
                tRt.anchorMin = new Vector2(0.28f, 0f);
                tRt.anchorMax = new Vector2(1f, 1f);
                tRt.offsetMin = new Vector2(2f, 2f);
                tRt.offsetMax = new Vector2(-4f, -2f);
            }
            else
            {
                FloatingPanel.Stretch(tRt, 2f);
            }
            return t;
        }

        public void RefreshFromSession()
        {
            var acc = AppSession.Ensure().Account;
            if (acc == null)
            {
                Set("—", "0", "0", "0", "—");
                return;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();
            var p = acc.progress;
            p.EnsureValid();
            var team = p.Team != KuribohTeam.None
                ? KuribohTeamInfo.DisplayName(p.Team)
                : "No Navi";
            var need = p.XpToNextLevel();
            var lv = need > 0
                ? $"Lv{Mathf.Max(1, p.level)} {Mathf.RoundToInt(p.XpProgress01() * 100)}%"
                : $"Lv{Mathf.Max(1, p.level)}";
            Set(lv,
                p.digizeni.ToString(),
                p.duelCoin.ToString(),
                p.setEnergy.ToString(),
                Short(team, 10));
        }

        static string Short(string s, int n) =>
            string.IsNullOrEmpty(s) ? "—" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        void Set(string lv, string digi, string coins, string energy, string navi)
        {
            if (_level != null) _level.text = lv;
            if (_digizeni != null) _digizeni.text = "Đ " + digi;
            if (_coins != null) _coins.text = "◎ " + coins;
            if (_energy != null) _energy.text = "⚡ " + energy;
            if (_navi != null) _navi.text = navi;
        }
    }
}
