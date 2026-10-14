using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>Pick a named AI opponent. Standard TCG 8000 LP. Dual presentation.</summary>
    public static class OpponentSelectScreen
    {
        public static RectTransform Build(
            Transform modalHost,
            Action onClose,
            bool labTest,
            UiPresentation? force = null,
            Action<OpponentDef> onPicked = null)
        {
            var frame = DualMenuPresenter.BuildFrame(
                modalHost, "CHOOSE YOUR OPPONENT", "Standard TCG · 8000 LP · your deck vs theirs", onClose, force);
            var body = frame.BodyHost;

            var status = FloatingPanel.Body(body, "Tap a name to duel.", 13);
            FloatingPanel.Place(status.rectTransform, 0.02f, 0.01f, 0.98f, 0.10f);
            status.alignment = TextAnchor.MiddleLeft;
            status.color = DuelystUi.Cyan;

            var scroll = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(body, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.01f, 0.12f, 0.99f, 0.99f);
            var bg = scroll.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = new Color(0.03f, 0.05f, 0.09f, 0.55f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(scroll.transform, false);
            FloatingPanel.Stretch(viewport.GetComponent<RectTransform>(), 4f);
            var vpImg = viewport.GetComponent<Image>();
            vpImg.sprite = UiFoundation.WhiteSprite();
            vpImg.color = new Color(1, 1, 1, 0.01f);
            vpImg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scroll.GetComponent<ScrollRect>();
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.content = crt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            void Pick(OpponentDef opp)
            {
                if (!labTest && !AppSession.Ensure().CanStartConstructedPvAi())
                {
                    FreeUiKit.PlayClick();
                    status.text = "ERAZ · LOCKED — finish tutorial";
                    return;
                }

                FreeUiKit.PlayConfirm();
                OpponentCatalog.Pending = opp;
                if (onPicked != null)
                {
                    onPicked(opp);
                    return;
                }

                var cfg = OpponentCatalog.MakeMatch(opp, labTest, digital: labTest);
                onClose?.Invoke();
                AppSession.Ensure().StartArDuel(cfg);
            }

            // ---- the duelists, each with its decks under its name (Yugi / Yami Yugi; Ryou Bakura /
            // Yami Bakura are separate seats; the Paradox Brothers are one seat, two bodies)
            foreach (var g in OpponentCatalog.Groups())
            {
                var team = g.members != null && g.members.Length > 1 ? "  ·  " + string.Join(" & ", g.members) : "";
                Header(content.transform, g.name + team);
                foreach (var opp in g.decks)
                {
                    if (opp == null) continue;
                    var missing = !OpponentCatalog.DeckFileExists(opp.file);
                    var o = opp;
                    Row(content.transform, o, missing ? null : () => Pick(o));
                }
            }

            // ---- today's street duelists: the random named duelists walking the overworld (NpcDuelistRoster)
            var street = NpcDuelistRoster.Today(8);
            if (street.Count > 0)
            {
                Header(content.transform, "STREET DUELISTS  ·  today");
                var streetGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<NpcDuelistRoster.NpcDuelist>>();
                foreach (var npc in street)
                {
                    var key = string.IsNullOrEmpty(npc.ArchetypeLabel) ? "Other" : npc.ArchetypeLabel;
                    if (!streetGroups.ContainsKey(key))
                        streetGroups[key] = new System.Collections.Generic.List<NpcDuelistRoster.NpcDuelist>();
                    streetGroups[key].Add(npc);
                }
                foreach (var group in streetGroups)
                {
                    if (streetGroups.Count > 1)
                        Header(content.transform, group.Key);
                    foreach (var npc in group.Value)
                    {
                        var opp = OpponentCatalog.ForStreet(npc);
                        var missing = !OpponentCatalog.DeckFileExists(opp.file);
                        var o = opp;
                        Row(content.transform, o, missing ? null : () => Pick(o));
                    }
                }
            }

            return frame.Root;
        }

        /// <summary>Full-screen overlay when MenuShell is not mounted (overworld fallback).</summary>
        public static GameObject OpenOverlay(Transform lifetimeHost, Action onClosed = null,
            Action<OpponentDef> onPicked = null)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();

            var canvas = WrldzTheme.Canvas("OpponentSelectCanvas", 90);
            EgyptianAgesAtmosphere.Attach(canvas, MenuAge.UmbraxRift, showAgeCaption: false);
            var host = canvas.gameObject;
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);

            void Close()
            {
                onClosed?.Invoke();
                if (host != null)
                    UnityEngine.Object.Destroy(host);
            }

            Build(canvas, Close, labTest: false, onPicked: onPicked);
            return host;
        }

        /// <summary>A duelist's name over its decks (not a button).</summary>
        static void Header(Transform host, string text)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = 36;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.10f, 0.14f, 0.24f, 0.95f);
            img.raycastTarget = false;
            var t = FloatingPanel.Body(go.transform, text.ToUpperInvariant(), 13);
            FloatingPanel.Place(t.rectTransform, 0.03f, 0.05f, 0.97f, 0.95f);
            t.alignment = TextAnchor.MiddleLeft;
            t.fontStyle = FontStyle.Bold;
            t.color = DuelystUi.Cyan;
        }

        static void Row(Transform host, OpponentDef opp, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = 72;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.05f, 0.08f, 0.14f, 0.9f);

            // 2D portrait (left). A tag-team row uses displayName (Para, Dox) so each deck shows that brother.
            var who = !string.IsNullOrEmpty(opp.displayName) ? opp.displayName : opp.name;
            var portrait = AvatarPortraitView.CreateFullBody(go.transform, 0.012f, 0.07f, 0.108f, 0.93f,
                hideBackground: false, badgeCrop: true);
            portrait.Apply(AiDuelistLooks.For(who));

            // name (bold) over deck · notes · LP
            var deck = !string.IsNullOrEmpty(opp.deckName) ? opp.deckName : who;
            var lp = opp.startingLp > 0 ? $" · {opp.startingLp} LP" : "";
            var label = $"{who}\n{deck} · {opp.notes}{lp}";
            var t = FloatingPanel.Body(go.transform, label, 13);
            FloatingPanel.Place(t.rectTransform, 0.125f, 0.07f, 0.80f, 0.93f);
            t.alignment = TextAnchor.MiddleLeft;
            t.color = DuelystUi.TextCream;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;

            // difficulty badge (right) — tap to cycle the tier for THIS duelist
            var eff = OpponentDifficultyPrefs.Get(opp);
            var badge = FloatingPanel.Body(go.transform, DifficultyLabel(eff), 11);
            FloatingPanel.Place(badge.rectTransform, 0.805f, 0.26f, 0.988f, 0.74f);
            badge.alignment = TextAnchor.MiddleCenter;
            badge.fontStyle = FontStyle.Bold;
            badge.color = DifficultyColor(eff);
            var badgeBtn = badge.gameObject.AddComponent<Button>();
            badgeBtn.targetGraphic = go.GetComponent<Image>();
            badgeBtn.onClick.AddListener(() =>
            {
                var nx = OpponentDifficultyPrefs.Cycle(opp);
                badge.text = DifficultyLabel(nx);
                badge.color = DifficultyColor(nx);

            });
            badge.gameObject.AddComponent<LayoutElement>().minWidth = 60;

            var btn = go.GetComponent<Button>();
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            else
                btn.interactable = false;
        }

        static string DifficultyLabel(AiDifficulty d) =>
            d == AiDifficulty.Easy ? "EASY" :
            d == AiDifficulty.Hard ? "HARD" :
            d == AiDifficulty.Expert ? "EXPERT" : "NORMAL";

        static Color DifficultyColor(AiDifficulty d) =>
            d == AiDifficulty.Easy ? new Color(0.35f, 0.85f, 0.45f) :
            d == AiDifficulty.Hard ? new Color(0.95f, 0.60f, 0.20f) :
            d == AiDifficulty.Expert ? new Color(0.95f, 0.30f, 0.30f) :
            DuelystUi.Cyan;
    }
}
