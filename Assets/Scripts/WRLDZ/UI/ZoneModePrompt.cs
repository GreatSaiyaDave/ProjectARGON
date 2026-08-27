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
            var root = WrldzTheme.StretchFill(canvas, "Dim", new Color(0.01f, 0.02f, 0.05f, 0.78f));
            root.GetComponent<Image>().raycastTarget = true;
            var host = canvas.gameObject;
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);

            void Close()
            {
                onClosed?.Invoke();
                if (host != null) UnityEngine.Object.Destroy(host);
            }

            Build(root, zoneId, zoneTitle, flavor, Close, kind, startingLp, street8000Locked, bossReady, bossLp, streak);
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
            var panel = FloatingPanel.Create(parent, "ZoneModePrompt", goldEdge: true);
            FloatingPanel.Place(panel, 0.08f, 0.22f, 0.92f, 0.80f);
            var pImg = panel.GetComponent<Image>();
            if (pImg != null)
            {
                var holo = ImagineAssets.MenuHoloSheet() ?? ImagineAssets.PanelModal() ?? pImg.sprite;
                if (holo != null)
                {
                    pImg.sprite = holo;
                    pImg.type = holo.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                }
                pImg.color = Color.white;
            }
            MenuHoloPulse.Attach(panel.gameObject, scan: true, breathe: false);
            MenuMotion.PlayOn(panel.gameObject, MenuMotion.PopIn(panel.gameObject, MenuMotion.Sheet));

            var accent = MapZoneCatalog.Accent(kind);
            var head = MapZoneCatalog.Label(kind);
            if (startingLp > 0) head += " · " + startingLp + " LP";
            var title = FloatingPanel.Title(panel, head, 18);
            FloatingPanel.Place(title.rectTransform, 0.06f, 0.86f, 0.94f, 0.97f);
            title.alignment = TextAnchor.MiddleCenter;
            title.color = accent;

            var name = FloatingPanel.Body(panel, zoneTitle ?? MapZoneCatalog.Label(kind), 15);
            FloatingPanel.Place(name.rectTransform, 0.08f, 0.76f, 0.92f, 0.86f);
            name.alignment = TextAnchor.MiddleCenter;
            name.color = DuelystUi.GoldHot;

            var purpose = string.IsNullOrEmpty(flavor)
                ? MapZoneCatalog.PurposeTitle(kind)
                : flavor;
            var body = FloatingPanel.Body(panel, purpose, 13);
            FloatingPanel.Place(body.rectTransform, 0.08f, 0.58f, 0.92f, 0.76f);
            body.alignment = TextAnchor.UpperCenter;
            body.color = DuelystUi.TextCream;

            var locked = street8000Locked;
            var raidSeats = 1;
            var raidFillAi = false;
            if (kind == MapZoneKind.Raid)
            {
                FloatingPanel.Place(body.rectTransform, 0.08f, 0.64f, 0.92f, 0.76f);
                var solo = FloatingPanel.PrimaryButton(panel, "SOLO", () =>
                {
                    raidSeats = 1;
                    raidFillAi = false;
                    FreeUiKit.PlayClick();
                }, gold: true);
                FloatingPanel.Place(solo.GetComponent<RectTransform>(), 0.08f, 0.52f, 0.48f, 0.62f);
                var trio = FloatingPanel.PrimaryButton(panel, "3-ON-1  AI", () =>
                {
                    raidSeats = 3;
                    raidFillAi = true;
                    FreeUiKit.PlayConfirm();
                });
                FloatingPanel.Place(trio.GetComponent<RectTransform>(), 0.52f, 0.52f, 0.92f, 0.62f);
            }

            var enterY0 = kind == MapZoneKind.Raid ? 0.34f : 0.36f;
            var enterY1 = kind == MapZoneKind.Raid ? 0.50f : 0.52f;
            var enterLabel = kind == MapZoneKind.Npc ? "ENCOUNTER" : "ENTER AR";
            var enterAr = FloatingPanel.PrimaryButton(panel, locked ? "LOCKED" : enterLabel, () =>
            {
                if (locked)
                {
                    FreeUiKit.PlayClick();
                    return;
                }

                FreeUiKit.PlayConfirm();
                Launch(zoneId, zoneTitle, digital: false, onClose, kind, startingLp, boss: false,
                    raidSeats, raidFillAi);
            }, gold: !locked);
            FloatingPanel.Place(enterAr.GetComponent<RectTransform>(), 0.08f, enterY0, 0.48f, enterY1);

            var digital = FloatingPanel.PrimaryButton(panel, locked ? "—" : "DIGITAL", () =>
            {
                if (locked)
                {
                    FreeUiKit.PlayClick();
                    return;
                }

                FreeUiKit.PlayConfirm();
                Launch(zoneId, zoneTitle, digital: true, onClose, kind, startingLp, boss: false,
                    raidSeats, raidFillAi);
            });
            FloatingPanel.Place(digital.GetComponent<RectTransform>(), 0.52f, enterY0, 0.92f, enterY1);

            if (kind == MapZoneKind.Tear)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var harvestReady = SetOrbService.TearHarvestReady(zoneId, now);
                var harvestLbl = harvestReady
                    ? "HARVEST"
                    : $"COOL {SetOrbService.TearHarvestRemainSec(zoneId, now)}s";
                var harvest = FloatingPanel.PrimaryButton(panel, harvestLbl, () =>
                {
                    if (!SetOrbService.TryHarvestTear(zoneId, out _, out var toast))
                    {
                        FreeUiKit.PlayClick();
                        body.text = toast ?? "Rift cooling.";
                        return;
                    }

                    FreeUiKit.PlayConfirm();
                    body.text = toast;
                }, gold: harvestReady);
                FloatingPanel.Place(harvest.GetComponent<RectTransform>(), 0.08f, 0.22f, 0.48f, 0.34f);

                var bossLbl = bossReady
                    ? $"TEAR BOSS · {bossLp} LP"
                    : $"BOSS  {streak}/{MapZoneCatalog.TearBossNeedStreak}";
                var boss = FloatingPanel.PrimaryButton(panel, bossLbl, () =>
                {
                    if (!bossReady)
                    {
                        FreeUiKit.PlayClick();
                        return;
                    }

                    FreeUiKit.PlayConfirm();
                    Launch(zoneId, zoneTitle, digital: false, onClose, kind, bossLp, boss: true);
                }, gold: bossReady);
                FloatingPanel.Place(boss.GetComponent<RectTransform>(), 0.52f, 0.22f, 0.92f, 0.34f);
            }

            var cancel = FloatingPanel.PrimaryButton(panel, "CANCEL", () =>
            {
                FreeUiKit.PlayClick();
                onClose?.Invoke();
            });
            FloatingPanel.Place(cancel.GetComponent<RectTransform>(), 0.25f, 0.04f, 0.75f, 0.16f);

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
                cfg = MapZoneService.MakeStoryEra(zoneId, zoneTitle, digital);
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
