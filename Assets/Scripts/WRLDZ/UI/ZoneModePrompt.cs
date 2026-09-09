using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Zone Mode encounter gate (PRODUCT_VISION + UI_SPEC §3.1).
    /// In range → modal: ENTER AR · DIGITAL · Cancel.
    /// Never auto-force AR; Digital = same duel without camera passthrough.
    /// </summary>
    public static class ZoneModePrompt
    {
        public static GameObject Open(
            Transform lifetimeHost,
            string zoneId,
            string zoneTitle,
            string flavor = null,
            Action onClosed = null,
            MapZoneKind kind = MapZoneKind.Tear,
            int startingLp = 0,
            bool street8000Locked = false,
            bool bossReady = false,
            int bossLp = 0,
            int streak = 0)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            var leftover = GameObject.Find("ZoneModePromptCanvas");
            if (leftover != null) UnityEngine.Object.DestroyImmediate(leftover);
            var canvas = WrldzTheme.Canvas("ZoneModePromptCanvas", 95);
            var host = canvas.gameObject;
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);

            void Close()
            {
                onClosed?.Invoke();
                if (host != null) UnityEngine.Object.Destroy(host);
            }

            Build(canvas, zoneId, zoneTitle, flavor, Close, kind, startingLp, street8000Locked, bossReady, bossLp, streak);
            return host;
        }

        public static RectTransform Build(
            Transform parent,
            string zoneId,
            string zoneTitle,
            string flavor,
            Action onClose,
            MapZoneKind kind = MapZoneKind.Tear,
            int startingLp = 0,
            bool street8000Locked = false,
            bool bossReady = false,
            int bossLp = 0,
            int streak = 0)
        {
            var accent = MapZoneCatalog.Accent(kind);
            var head = MapZoneCatalog.Label(kind);
            if (startingLp > 0) head += " · " + startingLp + " LP";
            var frame = DualMenuPresenter.BuildFrame(
                parent, head, zoneTitle ?? MapZoneCatalog.Label(kind), onClose);
            if (frame.Title != null) frame.Title.color = accent;
            var panel = frame.Root;
            panel.gameObject.name = "ZoneModePrompt";
            MenuMotion.PlayOn(panel.gameObject, MenuMotion.PopIn(panel.gameObject, MenuMotion.Sheet));
            var host = frame.BodyHost;

            var purpose = string.IsNullOrEmpty(flavor)
                ? MapZoneCatalog.PurposeTitle(kind)
                : flavor;
            var body = FloatingPanel.Body(host, purpose, 13);
            var purposeY0 = kind == MapZoneKind.Raid || kind == MapZoneKind.Tear ? 0.78f : 0.70f;
            FloatingPanel.Grid.Full(body.rectTransform, purposeY0, 0.98f);
            body.alignment = TextAnchor.UpperCenter;
            body.color = DuelystUi.TextCream;

            var locked = street8000Locked;
            var raidSeats = 1;
            var raidFillAi = false;
            if (kind == MapZoneKind.Raid)
            {
                var solo = HubChrome.Capsule(host, "SOLO", () =>
                {
                    raidSeats = 1;
                    raidFillAi = false;
                    FreeUiKit.PlayClick();
                }, MenuCommandButton.Kind.Gold, centerTitle: true, titleSize: 16);
                var trio = HubChrome.Capsule(host, "3-ON-1  AI", () =>
                {
                    raidSeats = 3;
                    raidFillAi = true;
                    FreeUiKit.PlayConfirm();
                }, MenuCommandButton.Kind.Primary, centerTitle: true, titleSize: 16);
                FloatingPanel.Grid.Pair(
                    solo.GetComponent<RectTransform>(),
                    trio.GetComponent<RectTransform>(),
                    0.62f, 0.74f);
            }

            var enterY0 = kind == MapZoneKind.Raid ? 0.46f : 0.52f;
            var enterY1 = enterY0 + 0.12f;
            var enterLabel = kind == MapZoneKind.Npc ? "ENCOUNTER" : "ENTER AR";
            var enterAr = HubChrome.Capsule(host, locked ? "LOCKED" : enterLabel, () =>
            {
                if (locked)
                {
                    FreeUiKit.PlayClick();
                    return;
                }

                FreeUiKit.PlayConfirm();
                Launch(zoneId, zoneTitle, digital: false, onClose, kind, startingLp, boss: false,
                    raidSeats, raidFillAi);
            }, locked ? MenuCommandButton.Kind.Secondary : MenuCommandButton.Kind.Gold,
                centerTitle: true, titleSize: 18);

            var digital = HubChrome.Capsule(host, locked ? "—" : "DIGITAL", () =>
            {
                if (locked)
                {
                    FreeUiKit.PlayClick();
                    return;
                }

                FreeUiKit.PlayConfirm();
                Launch(zoneId, zoneTitle, digital: true, onClose, kind, startingLp, boss: false,
                    raidSeats, raidFillAi);
            }, MenuCommandButton.Kind.Primary, centerTitle: true, titleSize: 18);
            FloatingPanel.Grid.Pair(
                enterAr.GetComponent<RectTransform>(),
                digital.GetComponent<RectTransform>(),
                enterY0, enterY1);

            if (kind == MapZoneKind.Tear)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var harvestReady = SetOrbService.TearHarvestReady(zoneId, now);
                var harvestLbl = harvestReady
                    ? "HARVEST"
                    : $"COOL {SetOrbService.TearHarvestRemainSec(zoneId, now)}s";
                var harvest = HubChrome.Capsule(host, harvestLbl, () =>
                {
                    if (!SetOrbService.TryHarvestTear(zoneId, out _, out var toast))
                    {
                        FreeUiKit.PlayClick();
                        body.text = toast ?? "Rift cooling.";
                        return;
                    }

                    FreeUiKit.PlayConfirm();
                    body.text = toast;
                }, harvestReady ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Secondary,
                    centerTitle: true, titleSize: 16);

                var bossLbl = bossReady
                    ? $"TEAR BOSS · {bossLp} LP"
                    : $"BOSS  {streak}/{MapZoneCatalog.TearBossNeedStreak}";
                var boss = HubChrome.Capsule(host, bossLbl, () =>
                {
                    if (!bossReady)
                    {
                        FreeUiKit.PlayClick();
                        return;
                    }

                    FreeUiKit.PlayConfirm();
                    Launch(zoneId, zoneTitle, digital: false, onClose, kind, bossLp, boss: true);
                }, bossReady ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Secondary,
                    centerTitle: true, titleSize: 16);
                FloatingPanel.Grid.Pair(
                    harvest.GetComponent<RectTransform>(),
                    boss.GetComponent<RectTransform>(),
                    0.36f, 0.48f);
            }

            var cancel = HubChrome.Capsule(host, "CANCEL", () =>
            {
                FreeUiKit.PlayClick();
                onClose?.Invoke();
            }, MenuCommandButton.Kind.Secondary, centerTitle: true, titleSize: 16);
            FloatingPanel.Grid.Full(cancel.GetComponent<RectTransform>(), 0.06f, 0.18f);

            return panel;
        }

        static void Launch(string zoneId, string zoneTitle, bool digital, Action onClose,
            MapZoneKind kind, int startingLp, bool boss, int raidSeats = 1, bool raidFillAi = false)
        {
            ArDuelMatchConfig cfg;
            if (kind == MapZoneKind.Training)
                cfg = MapZoneService.MakeTrainingConfig(zoneTitle);
            else if (kind == MapZoneKind.Npc)
            {
                var band = startingLp >= MapZoneCatalog.Street8000Lp
                    ? StreetLpBand.Street8000
                    : StreetLpBand.Street4000;
                cfg = MapZoneService.MakeStreetNpc(zoneId, zoneTitle, band, digital);
            }
            else if (kind == MapZoneKind.Raid)
                cfg = MapZoneService.MakeRaidBoss(zoneId, zoneTitle, digital, raidSeats, raidFillAi);
            else if (kind == MapZoneKind.Story)
            {
                var acc = AppSession.Ensure()?.Account;
                acc?.EnsureProgress();
                var stage = StoryCampaignService.Current(acc?.progress);
                cfg = MapZoneService.MakeStoryEra(stage, zoneId, digital);
            }
            else if (kind == MapZoneKind.Pvp)
                cfg = MapZoneService.MakePvpZone(zoneTitle);
            else if (kind == MapZoneKind.Tear && boss)
                cfg = MapZoneService.MakeTearBoss(zoneId, zoneTitle, digital);
            else
                cfg = MapZoneService.MakeTearConfig(zoneId, zoneTitle, digital);

            cfg.PreferDigital = digital;
            onClose?.Invoke();
            AppSession.Ensure().StartArDuel(cfg);
        }
    }
}
