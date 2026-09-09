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
            HubPropView.DestroyAll();
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

            var dusk = WrldzTheme.StretchFill(root, "LowerDusk", new Color(0.02f, 0.03f, 0.08f, 0.42f));
            GoTheme.Place(dusk, 0f, 0f, 1f, 0.58f);
            dusk.GetComponent<Image>().raycastTarget = false;

            var header = GoTheme.Sheet(root, "Header", 0.04f, 0.84f, 0.96f, 0.978f);
            if (header.GetComponent<UnityEngine.UI.RectMask2D>() == null)
                header.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

            var faceHost = new GameObject("FaceHost", typeof(RectTransform), typeof(Image), typeof(Button));
            faceHost.transform.SetParent(header, false);
            GoTheme.Place(faceHost.GetComponent<RectTransform>(), 0.03f, 0.08f, 0.22f, 0.92f);
            var faceImg = faceHost.GetComponent<Image>();
            faceImg.sprite = DuelystUi.OrbRing() ?? DuelystUi.BtnCircle();
            faceImg.color = Color.white;
            faceImg.preserveAspect = true;
            var portrait = AvatarPortraitView.CreateFullBodyFill(faceHost.transform, hideBackground: true, badgeCrop: true);
            portrait.Apply(account != null ? account.GetAvatarOrDefault() : AvatarAppearance.Default());
            faceHost.GetComponent<Button>().onClick.AddListener(OpenAvatarMenus);

            var look = account != null ? account.GetAvatarOrDefault() : null;
            var title = GoTheme.Label(header, "Title", who, 28, DuelystUi.GoldHot, TextAnchor.MiddleLeft);
            WrldzType.StyleGoldTitle(title, 28);
            title.alignment = TextAnchor.MiddleLeft;
            GoTheme.Place(title.rectTransform, 0.25f, 0.46f, 0.97f, 0.94f);
            var sub = GoTheme.Label(header, "Sub",
                $"Lv{lvl}  ·  {team}  ·  {(look?.title ?? "Spirit Dueler")}", 16,
                DuelystUi.TextCream, TextAnchor.MiddleLeft, bold: true);
            WrldzType.Style(sub, 16, display: false, heavyOutline: true);
            sub.color = DuelystUi.TextCream;
            WrldzType.ApplyOutline(sub, heavy: true, buttonContrast: true);
            GoTheme.Place(sub.rectTransform, 0.25f, 0.08f, 0.97f, 0.48f);

            HubChrome.SectionCap(root, "DuelCap", "DUEL", DuelystUi.GoldHot, 0.06f, 0.795f, 0.40f, 0.838f);

            FeaturedTile(root, 0.04f, 0.555f, 0.49f, 0.790f,
                "VS AI", "Practice · table · street", ImagineAssets.IconDuel(),
                OpenPlayerVsAi, gold: true, propStem: null, useDisk: true);
            FeaturedTile(root, 0.51f, 0.555f, 0.96f, 0.790f,
                "VS PLAYER", "Scan · shared arena", ImagineAssets.IconVsPvp(),
                OpenPlayerVsPlayer, gold: false, propStem: "hub_prop_pvp", useDisk: false);

            HubChrome.SectionCap(root, "SysCap", "COMMAND", DuelystUi.Cyan, 0.06f, 0.508f, 0.46f, 0.548f);

            DestTile(root, 0.04f, 0.330f, 0.34f, 0.492f, "DECK", ImagineAssets.IconDeck(),
                OpenDeckHub, "hub_prop_deckbox");
            DestTile(root, 0.355f, 0.330f, 0.645f, 0.492f, "BAG", ImagineAssets.IconBag(),
                ShowInventory, "hub_prop_bag");
            DestTile(root, 0.66f, 0.330f, 0.96f, 0.492f, "STORY", ImagineAssets.IconStory(),
                OpenStory, "hub_prop_tome");
            DestTile(root, 0.04f, 0.160f, 0.34f, 0.322f, "BAZAAR", ImagineAssets.IconBazaar(),
                OpenBazaar, "hub_prop_bazaar");
            DestTile(root, 0.355f, 0.160f, 0.645f, 0.322f, "VIEW", ImagineAssets.IconView(),
                OpenFreeView, "hub_prop_view");
            DestTile(root, 0.66f, 0.160f, 0.96f, 0.322f, "SET", ImagineAssets.IconSettings(),
                OpenSettings, "hub_prop_settings");

            _status = GoTheme.Label(root, "S", "", 14, DuelystUi.TextMuted, TextAnchor.MiddleCenter, bold: false);
            WrldzType.Style(_status, 14, display: false, heavyOutline: true);
            GoTheme.Place(_status.rectTransform, 0.08f, 0.128f, 0.92f, 0.154f);

            var mapBtn = MenuCommandButton.Create(root, "BATTLE CITY MAP", () =>
            {
                FreeUiKit.PlayConfirm();
                AppSession.Ensure().GoOverworld();
            }, MenuCommandButton.Kind.Gold, plated: true);
            mapBtn.name = "MapBack";
            GoTheme.Place(mapBtn.GetComponent<RectTransform>(), 0.18f, 0.012f, 0.82f, 0.125f);
            MenuCommandButton.ApplyHubType(mapBtn, 20, DuelystUi.GoldHot, displayTitle: true);
            var mapTitle = mapBtn.transform.Find("Title") as RectTransform;
            if (mapTitle != null)
                GoTheme.Place(mapTitle, 0.28f, 0.12f, 0.94f, 0.88f);
            HubChrome.LiftPlate(mapBtn.GetComponent<Image>(), DuelystUi.Gold);
            MenuHoloPulse.Attach(mapBtn.gameObject, scan: true, breathe: false, phase: 0.4f);

            var compass = HubPropView.CreateInUi(mapBtn.transform, "hub_prop_compass",
                0.04f, 0.10f, 0.26f, 0.90f, DuelystUi.GoldHot, HubPropView.DestRt);
            if (compass == null)
            {
                var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(mapBtn.transform, false);
                GoTheme.Place(ico.GetComponent<RectTransform>(), 0.04f, 0.12f, 0.26f, 0.88f);
                var img = ico.GetComponent<Image>();
                img.sprite = ImagineAssets.IconCompass() ?? WrldzPresentation.SpiritEye() ?? DuelystUi.IconDeck();
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
        }

        void FeaturedTile(Transform parent, float x0, float y0, float x1, float y1,
            string title, string blurb, Sprite icon, Action onClick, bool gold,
            string propStem, bool useDisk)
        {
            var btn = HubChrome.MountFeatured(parent, title, blurb, useDisk ? null : icon, onClick, gold,
                useDisk ? null : propStem);
            GoTheme.Place(btn.GetComponent<RectTransform>(), x0, y0, x1, y1);
            if (!useDisk) return;
            _hubDisk = SpiritDuelerDiskView.CreateInUi(btn.transform,
                SpiritDuelerDiskView.PoseMode.HubShowcase,
                0.62f, 0.08f, 0.97f, 0.92f, DuelystUi.GoldHot,
                HubPropView.EnsureWorldRoot(), false);
        }

        void DestTile(Transform parent, float x0, float y0, float x1, float y1,
            string title, Sprite icon, Action onClick, string propStem)
        {
            var btn = HubChrome.MountDest(parent, title, icon, onClick, propStem);
            GoTheme.Place(btn.GetComponent<RectTransform>(), x0, y0, x1, y1);
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

        void Toast(string msg)
        {
            if (_status != null) _status.text = msg;
            Debug.Log("[WRLDZ] Hub: " + msg);
        }
    }
}
