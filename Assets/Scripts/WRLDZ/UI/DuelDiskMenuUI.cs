using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;
// DuelystUi = coordinated CCG chrome

namespace WRLDZ.UI
{
    /// <summary>
    /// Hub menu (MainMenu scene) — piano-glass tiles on a Battle City rooftop.
    /// Duel actions first, then a 2×3 destination grid. MAP returns home.
    /// </summary>
    public class DuelDiskMenuUI : MonoBehaviour
    {
        public string duelSceneName = "DuelSlice";
        public const string GameTitle = "Duel Monsters: WRLDZ";
        public const string ProjectCode = "ProjectARGON";

        Text _status;
        SpiritDuelerDiskView _hubDisk;

        public void Build()
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            GoTheme.EnsureAssets();
            WrldzTheme.ApplyPortrait();
            BuildVisuals();
        }

        void OnDestroy()
        {
            if (_hubDisk != null)
                Destroy(_hubDisk.gameObject);
        }

        void BuildVisuals()
        {
            var canvas = WrldzTheme.Canvas("DiskMenuCanvas", 50);

            var bg = WrldzTheme.StretchFill(canvas, "HubBg", Color.white, ImagineAssets.BgHub());
            var bgImg = bg.GetComponent<Image>();
            bgImg.preserveAspect = false;
            bgImg.raycastTarget = false;

            var root = WrldzTheme.StretchFill(canvas, "Root", new Color(0.02f, 0.03f, 0.07f, 0.38f));
            root.GetComponent<Image>().raycastTarget = false;

            var account = AppSession.Ensure().Account;
            account?.EnsureProgress();
            var who = account?.displayName ?? "Duelist";
            var lvl = account?.progress?.level ?? 1;
            var team = account != null
                ? KuribohTeamInfo.DisplayName(account.progress.Team)
                : "Unbound";

            var header = GoTheme.Sheet(root, "Header", 0.04f, 0.80f, 0.96f, 0.97f);

            var faceHost = new GameObject("FaceHost", typeof(RectTransform), typeof(Image), typeof(Button));
            faceHost.transform.SetParent(header, false);
            GoTheme.Place(faceHost.GetComponent<RectTransform>(), 0.04f, 0.10f, 0.24f, 0.90f);
            var faceImg = faceHost.GetComponent<Image>();
            faceImg.sprite = DuelystUi.OrbRing() ?? DuelystUi.BtnCircle();
            faceImg.color = Color.white;
            faceImg.preserveAspect = true;
            var portrait = AvatarPortraitView.CreateFullBodyFill(faceHost.transform, hideBackground: true, badgeCrop: true);
            portrait.Apply(account != null ? account.GetAvatarOrDefault() : AvatarAppearance.Default());
            faceHost.GetComponent<Button>().onClick.AddListener(OpenAvatarMenus);

            var look = account != null ? account.GetAvatarOrDefault() : null;
            var title = GoTheme.Label(header, "Title", who, 26, Color.white, TextAnchor.MiddleLeft);
            title.font = UiFoundation.BuiltinFont();
            title.color = Color.white;
            WrldzType.ApplyOutline(title, heavy: true, buttonContrast: true);
            GoTheme.Place(title.rectTransform, 0.28f, 0.48f, 0.96f, 0.92f);
            var sub = GoTheme.Label(header, "Sub",
                $"Lv{lvl}  ·  {team}  ·  {(look?.title ?? "Spirit Dueler")}", 16,
                new Color(0.85f, 0.90f, 0.96f, 1f), TextAnchor.MiddleLeft, bold: true);
            sub.font = UiFoundation.BuiltinFont();
            WrldzType.ApplyOutline(sub, heavy: true, buttonContrast: true);
            GoTheme.Place(sub.rectTransform, 0.28f, 0.08f, 0.96f, 0.48f);

            var duelCap = GoTheme.Label(root, "DuelCap", "DUEL", 15,
                DuelystUi.Gold, TextAnchor.MiddleLeft);
            duelCap.font = UiFoundation.BuiltinFont();
            WrldzType.ApplyOutline(duelCap, heavy: true, buttonContrast: true);
            GoTheme.Place(duelCap.rectTransform, 0.07f, 0.75f, 0.5f, 0.79f);

            FeaturedTile(root, 0.05f, 0.52f, 0.49f, 0.75f,
                "VS AI", "Practice · table · street", ImagineAssets.IconDuel(), OpenPlayerVsAi, gold: true);
            FeaturedTile(root, 0.51f, 0.52f, 0.95f, 0.75f,
                "VS PLAYER", "Scan · shared arena", ImagineAssets.IconDuel(), OpenPlayerVsPlayer, gold: false);

            var sysCap = GoTheme.Label(root, "SysCap", "COMMAND", 15,
                DuelystUi.Cyan, TextAnchor.MiddleLeft);
            sysCap.font = UiFoundation.BuiltinFont();
            WrldzType.ApplyOutline(sysCap, heavy: true, buttonContrast: true);
            GoTheme.Place(sysCap.rectTransform, 0.07f, 0.47f, 0.5f, 0.51f);

            DestTile(root, 0.05f, 0.31f, 0.34f, 0.47f, "DECK", ImagineAssets.IconDeck(), OpenDeckHub, false);
            DestTile(root, 0.355f, 0.31f, 0.645f, 0.47f, "BAG", ImagineAssets.IconBag(), ShowInventory, false);
            DestTile(root, 0.66f, 0.31f, 0.95f, 0.47f, "STORY", ImagineAssets.IconStory(), OpenStory, false);
            DestTile(root, 0.05f, 0.14f, 0.34f, 0.30f, "BAZAAR", ImagineAssets.IconBazaar(), OpenBazaar, false);
            DestTile(root, 0.355f, 0.14f, 0.645f, 0.30f, "VIEW", ImagineAssets.IconDuel(), OpenFreeView, false);
            DestTile(root, 0.66f, 0.14f, 0.95f, 0.30f, "SET", ImagineAssets.IconSettings(), OpenSettings, false);

            var bot = new GameObject("BotBar", typeof(RectTransform), typeof(Image));
            bot.transform.SetParent(root, false);
            GoTheme.Place(bot.GetComponent<RectTransform>(), 0.04f, 0.005f, 0.96f, 0.12f);
            var botImg = bot.GetComponent<Image>();
            botImg.sprite = ImagineAssets.BarBottom() ?? UiFoundation.WhiteSprite();
            botImg.type = botImg.sprite != null && botImg.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            botImg.color = Color.white;
            botImg.raycastTarget = false;

            var mapBtn = GoTheme.CircleButton(root, "MapBack",
                DuelystUi.BtnCircle(), Color.white,
                0.38f, 0.012f, 0.62f, 0.115f, () =>
                {
                    FreeUiKit.PlayConfirm();
                    AppSession.Ensure().GoOverworld();
                });
            GoTheme.SetCenterIcon(mapBtn.transform,
                ImagineAssets.IconCompass() ?? WrldzPresentation.SpiritEye() ?? DuelystUi.IconDeck(), 0.52f);

            _status = GoTheme.Label(bot.transform, "S", "MAP", 12,
                DuelystUi.TextMuted, TextAnchor.MiddleCenter, bold: false);
            GoTheme.Place(_status.rectTransform, 0.05f, 0.04f, 0.95f, 0.36f);
        }

        void FeaturedTile(Transform parent, float x0, float y0, float x1, float y1,
            string title, string blurb, Sprite icon, Action onClick, bool gold)
        {
            var kind = gold ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Primary;
            var btn = MenuCommandButton.Create(parent, title, onClick, kind, blurb);
            GoTheme.Place(btn.GetComponent<RectTransform>(), x0, y0, x1, y1);
            if (icon == null) return;
            var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
            ico.transform.SetParent(btn.transform, false);
            GoTheme.Place(ico.GetComponent<RectTransform>(), 0.72f, 0.18f, 0.94f, 0.82f);
            var img = ico.GetComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                GoTheme.Place(titleRt, 0.06f, 0.48f, 0.70f, 0.92f);
            var blurbRt = btn.transform.Find("Blurb") as RectTransform;
            if (blurbRt != null)
                GoTheme.Place(blurbRt, 0.06f, 0.08f, 0.70f, 0.46f);
        }

        void DestTile(Transform parent, float x0, float y0, float x1, float y1,
            string title, Sprite icon, Action onClick, bool gold)
        {
            var kind = gold ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Primary;
            var btn = MenuCommandButton.Create(parent, title, onClick, kind, centerTitle: true);
            GoTheme.Place(btn.GetComponent<RectTransform>(), x0, y0, x1, y1);
            if (icon == null) return;
            var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
            ico.transform.SetParent(btn.transform, false);
            GoTheme.Place(ico.GetComponent<RectTransform>(), 0.22f, 0.38f, 0.78f, 0.88f);
            var iimg = ico.GetComponent<Image>();
            iimg.sprite = icon;
            iimg.preserveAspect = true;
            iimg.raycastTarget = false;
            var titleRt = btn.transform.Find("Title") as RectTransform;
            if (titleRt != null)
                GoTheme.Place(titleRt, 0.06f, 0.06f, 0.94f, 0.36f);
        }

        /// <summary>Hub primary: Player vs AI create → START → AR DuelSlice.</summary>
        void OpenPlayerVsAi() => OpenHubOverlay(MenuId.ArDuelCreate);

        /// <summary>Hub: Player vs Player distance scan → START → AR DuelSlice (hotseat).</summary>
        void OpenPlayerVsPlayer() => OpenHubOverlay(MenuId.ArDuelPvpCreate);

        void OpenFreeView() => OpenHubOverlay(MenuId.FreeView);

        void OpenDeckHub() => OpenHubOverlay(MenuId.DeckCollection);

        void OpenBazaar() => OpenHubOverlay(MenuId.Bazaar);

        void OpenStory() => OpenHubOverlay(MenuId.StorySeason);

        void OpenSettings() => OpenHubOverlay(MenuId.Settings);

        /// <summary>One overlay shell for every hub sheet — no stacked canvases, no toast stubs.</summary>
        void OpenHubOverlay(MenuId id)
        {
            FreeUiKit.PlaySelect();
            ScreenRouter.Ensure().Presentation = UiPresentation.NonArPortrait;
            var shell = EnsureHubShell();
            ScreenRouter.Ensure(shell);
            shell.ShowOverlay(id);
        }

        MenuShell _hubShell;

        MenuShell EnsureHubShell()
        {
            if (_hubShell != null) return _hubShell;
            var existing = FindObjectsByType<MenuShell>(FindObjectsSortMode.None);
            foreach (var s in existing)
            {
                if (s != null && s.OverlayOnly)
                {
                    _hubShell = s;
                    return _hubShell;
                }
            }

            _hubShell = MenuShell.CreateOverlay(80);
            return _hubShell;
        }

        void OpenAvatarMenus()
        {
            FreeUiKit.PlaySelect();
            var ui = AvatarCustomizerUI.Ensure(transform);
            ui.OpenProfile(() =>
            {
                AppSession.Ensure().RefreshFromStore();
                Toast("Avatar saved. Re-open hub to refresh header portrait.");
            });
        }

        void ShowInventory()
        {
            FreeUiKit.PlaySelect();
            var acc = AppSession.Ensure().Account;
            if (acc == null)
            {
                Toast("No account.");
                return;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();

            OpenHubOverlay(MenuId.Inventory);
        }

        void ShowTome()
        {
            var acc = AppSession.Ensure().Account;
            if (acc != null)
            {
                TomeService.SyncCapacityUnlocks(acc);
                AppSession.Ensure().RefreshFromStore();
            }

            OpenHubOverlay(MenuId.TomeRaid);
        }

        void Toast(string msg)
        {
            if (_status != null) _status.text = msg;
            Debug.Log("[WRLDZ] Hub: " + msg);
        }
    }
}
