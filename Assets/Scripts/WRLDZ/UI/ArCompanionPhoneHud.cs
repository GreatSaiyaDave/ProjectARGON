using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Phone companion during AR duels / Zone Mode AR:
    /// profile + non-duel systems (decks, backpack, tome, artifacts, settings).
    /// Card play stays on the AR field / Spirit Dueler disk — not this screen.
    /// </summary>
    public static class ArCompanionPhoneHud
    {
        public sealed class Host
        {
            public RectTransform Root;
            public RectTransform ModalHost;
            public Text StatusLine;
            public Text YouLp;
            public Text OppLp;
            public Text PhaseLabel;
            public Action RefreshProfile;
        }

        public static Host Build(Transform parent, Action onLeaveDuel, Action onOpenReview = null)
        {
            FreeUiKit.EnsureLoaded();
            var root = new GameObject("ArCompanionPhone", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            FloatingPanel.Stretch(root);

            // Combat LP / phase / status live on DuelFloatingHud — this sheet is systems only.
            Text youLp = null;
            Text phase = null;
            Text oppLp = null;
            Text status = null;

            // ── Profile / systems sheet — starts HIDDEN (toggle with Systems) ──
            var sheet = FloatingPanel.Create(root, "ProfileSheet", goldEdge: true);
            FloatingPanel.Place(sheet, 0.06f, 0.12f, 0.94f, 0.86f);
            sheet.gameObject.SetActive(false);
            var sheetImg = sheet.GetComponent<Image>();
            if (sheetImg != null) sheetImg.color = new Color(0.05f, 0.07f, 0.11f, 0.72f);

            // Avatar ring
            var faceHost = new GameObject("Avatar", typeof(RectTransform), typeof(Image), typeof(Button));
            faceHost.transform.SetParent(sheet, false);
            FloatingPanel.Place(faceHost.GetComponent<RectTransform>(), 0.06f, 0.72f, 0.28f, 0.96f);
            var faceImg = faceHost.GetComponent<Image>();
            faceImg.sprite = DuelystUi.OrbRing() ?? DuelystUi.BtnCircle();
            faceImg.color = Color.white;
            faceImg.preserveAspect = true;
            var portrait = AvatarPortraitView.CreateFullBodyFill(faceHost.transform, hideBackground: true, badgeCrop: true);

            var nameT = FloatingPanel.Body(sheet, "Duelist", 22);
            FloatingPanel.Place(nameT.rectTransform, 0.32f, 0.86f, 0.94f, 0.96f);
            nameT.color = DuelystUi.GoldHot;
            nameT.alignment = TextAnchor.MiddleLeft;
            WrldzType.StyleButtonLabel(nameT, 20, display: true);

            var subT = FloatingPanel.Body(sheet, "Lv1 · Team", 15);
            FloatingPanel.Place(subT.rectTransform, 0.32f, 0.76f, 0.94f, 0.86f);
            subT.color = Color.white;
            subT.alignment = TextAnchor.MiddleLeft;

            var currT = FloatingPanel.Body(sheet, "Đ0 · DC0 · SE0", 14);
            FloatingPanel.Place(currT.rectTransform, 0.32f, 0.68f, 0.88f, 0.76f);
            currT.color = DuelystUi.Cyan;
            currT.alignment = TextAnchor.MiddleLeft;

            var soul = SoulBadgeView.Create(sheet);
            FloatingPanel.Place(soul.GetComponent<RectTransform>(), 0.86f, 0.78f, 0.98f, 0.96f);

            var note = FloatingPanel.Body(sheet,
                "AR MODE · Card play is on the Spirit Dueler / midfield holos.\n" +
                "This screen is profile + systems — not the duel board.",
                12);
            FloatingPanel.Place(note.rectTransform, 0.06f, 0.56f, 0.94f, 0.67f);
            note.color = DuelystUi.TextMuted;
            note.alignment = TextAnchor.UpperLeft;

            // Modal host for nested menus (decks, bag, settings)
            var modal = new GameObject("ModalHost", typeof(RectTransform)).GetComponent<RectTransform>();
            modal.SetParent(root, false);
            FloatingPanel.Stretch(modal);

            void ClearModal()
            {
                FloatingPanel.DestroyChildrenNow(modal);
            }

            // Menu grid
            float y = 0.50f;
            void Tile(string label, string blurb, Action open, bool gold = false)
            {
                var b = FloatingPanel.PrimaryButton(sheet, label, () =>
                {
                    FreeUiKit.PlaySelect();
                    ClearModal();
                    open?.Invoke();
                }, gold: gold);
                FloatingPanel.Place(b.GetComponent<RectTransform>(), 0.06f, y - 0.09f, 0.94f, y);
                // Blurb as button sub-label is tight — put blurb in toast/status
                y -= 0.10f;
            }

            Tile("DECK", NavCopy.BlurbFor(MenuId.DeckCollection), () =>
            {
                if (status != null)
                    status.text = NavCopy.ToastFor(MenuId.DeckCollection, UiPresentation.ArDiskHolo);
                DeckCollectionScreen.Build(modal, ClearModal, UiPresentation.ArDiskHolo);
            }, gold: true);

            Tile("BACKPACK", NavCopy.BlurbFor(MenuId.Inventory), () =>
            {
                if (status != null)
                    status.text = NavCopy.ToastFor(MenuId.Inventory);
                InventoryScreen.Build(modal, ClearModal);
            });

            Tile("TOME", NavCopy.BlurbFor(MenuId.TomeRaid), () =>
            {
                if (status != null)
                    status.text = NavCopy.ToastFor(MenuId.TomeRaid);
                SystemsSheets.BuildTome(modal, ClearModal, UiPresentation.ArDiskHolo);
            });

            Tile("ARTIFACTS", "Key items · artifact deck box", () =>
            {
                ClearModal();
                var acc = AppSession.Ensure().Account;
                acc?.EnsureInventory();
                var inv = acc?.inventory;
                inv?.EnsureValid();
                var art = inv?.artifactDeckBox;
                var owned = art != null && art.owned;
                var n = art?.artifactCardIds?.Length ?? 0;
                var where = art == null || art.atHome ? "at home" : "in pack";
                if (status != null)
                    status.text = owned
                        ? $"Artifacts · {n} cards · box {where}"
                        : "Artifact deck box locked";
                ShowInfoSheet(modal, "ARTIFACTS",
                    owned
                        ? $"Artifact Deck Box: owned\nLocation: {where}\nArtifact cards: {n}\n\n" +
                          "× closes · key-item equip UI later."
                        : "No Artifact Deck Box yet.\nGranted with starter kit / story.\n× closes.",
                    ClearModal);
            });

            Tile("SETTINGS", NavCopy.BlurbFor(MenuId.Settings), () =>
            {
                if (status != null)
                    status.text = NavCopy.ToastFor(MenuId.Settings);
                SettingsScreen.Build(modal, null, ClearModal);
            });

            if (onOpenReview != null)
            {
                Tile("DUEL LOG", "Read-only lines · × / hide to return", () =>
                {
                    ClearModal();
                    onOpenReview.Invoke();
                });
            }

            var leaveOnSheet = FloatingPanel.PrimaryButton(sheet, "← MAP / LEAVE DUEL", () =>
            {
                FreeUiKit.PlayConfirm();
                onLeaveDuel?.Invoke();
            }, danger: true);
            FloatingPanel.Place(leaveOnSheet.GetComponent<RectTransform>(), 0.12f, 0.02f, 0.88f, 0.10f);

            // Tiny chips beside the floating action island — never over disk / holos.
            var systems = FloatingPanel.PrimaryButton(root, "SYS", () =>
            {
                FreeUiKit.PlaySelect();
                var open = !sheet.gameObject.activeSelf;
                sheet.gameObject.SetActive(open);
                if (!open) ClearModal();
                if (status != null)
                    status.text = open ? "Systems open · AR field behind" : "AR field · disks + holos";
            }, centerLabel: true);
            // Compact capsules, equal inset from each edge, same band as phase CTAs.
            DressFab(systems, ImagineAssets.IconArenaSys());
            FloatingPanel.Place(systems.GetComponent<RectTransform>(), 0.012f, 0.132f, 0.066f, 0.188f);

            var leaveFab = FloatingPanel.PrimaryButton(root, "MAP", () =>
            {
                FreeUiKit.PlayConfirm();
                onLeaveDuel?.Invoke();
            }, danger: true, centerLabel: true);
            DressFab(leaveFab, ImagineAssets.IconCompass());
            FloatingPanel.Place(leaveFab.GetComponent<RectTransform>(), 0.934f, 0.132f, 0.988f, 0.188f);

            // Close systems when tapping… (sheet has leave; Systems toggles)

            faceHost.GetComponent<Button>().onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                ClearModal();
                AvatarCustomizerUI.Ensure().OpenProfile(() => Refresh());
            });

            void Refresh()
            {
                var acc = AppSession.Ensure().Account;
                acc?.EnsureProgress();
                acc?.EnsureInventory();
                var p = acc?.progress;
                var who = acc?.displayName ?? acc?.username ?? "Duelist";
                var lvl = p?.level ?? 1;
                var team = p != null
                    ? KuribohTeamInfo.DisplayName(p.Team)
                    : "—";
                var title = acc != null ? acc.GetAvatarOrDefault()?.title : null;
                if (string.IsNullOrEmpty(title)) title = "Spirit Dueler";

                nameT.text = who;
                subT.text = $"Lv{lvl}  ·  {team}  ·  {title}";
                currT.text =
                    $"Đ{p?.digizeni ?? 0}  ·  DC{p?.duelCoin ?? 0}  ·  SE{p?.setEnergy ?? 0}";
                soul.Bind(p);

                if (portrait != null)
                    portrait.Apply(acc != null ? acc.GetAvatarOrDefault() : AvatarAppearance.Default());
            }

            Refresh();

            return new Host
            {
                Root = root,
                ModalHost = modal,
                StatusLine = status,
                YouLp = youLp,
                OppLp = oppLp,
                PhaseLabel = phase,
                RefreshProfile = Refresh
            };
        }

        static void DressFab(Button btn, Sprite icon)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            var orb = ImagineAssets.HudFabOrb();
            if (img != null && orb != null)
            {
                img.sprite = orb;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = Color.white;
                var ol = btn.GetComponent<Outline>();
                if (ol != null) ol.enabled = false;
            }
            var title = btn.transform.Find("Title");
            if (title != null) title.gameObject.SetActive(false);
            if (icon == null) return;
            var ico = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            ico.transform.SetParent(btn.transform, false);
            FloatingPanel.Place(ico.GetComponent<RectTransform>(), 0.20f, 0.20f, 0.80f, 0.80f);
            var ii = ico.GetComponent<Image>();
            ii.sprite = icon;
            ii.preserveAspect = true;
            ii.raycastTarget = false;
            ii.color = Color.white;
        }

        static void ShowInfoSheet(Transform modalHost, string title, string body, Action onClose)
        {
            var panel = FloatingPanel.Create(modalHost, "Info_" + title, goldEdge: true);
            FloatingPanel.Place(panel, 0.06f, 0.22f, 0.94f, 0.78f);
            var t = FloatingPanel.Title(panel, title, 22);
            FloatingPanel.Place(t.rectTransform, 0.06f, 0.82f, 0.94f, 0.96f);
            var b = FloatingPanel.Body(panel, body, 15);
            FloatingPanel.Place(b.rectTransform, 0.08f, 0.22f, 0.92f, 0.80f);
            b.alignment = TextAnchor.UpperLeft;
            b.color = DuelystUi.TextCream;
            var close = FloatingPanel.PrimaryButton(panel, "CLOSE", onClose);
            FloatingPanel.Place(close.GetComponent<RectTransform>(), 0.25f, 0.06f, 0.75f, 0.16f);
        }
    }
}
