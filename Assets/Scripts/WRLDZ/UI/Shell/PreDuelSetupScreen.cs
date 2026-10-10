using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// The pre-duel setup sheet (DuelSlice, before the pre-duel cinematic): first-turn method, deck, ERAZ band and
    /// duel format, with the deck checked against the chosen format card by card. Same chrome as every other sheet
    /// (<see cref="DualMenuPresenter.BuildFrame"/>, rows as <see cref="OpponentSelectScreen"/>); locked options use
    /// the project convention (listed, non-interactive, LOCKED in the danger colour). The values confirmed here are
    /// written onto the match config and are the values the duel then starts with (<see cref="DuelBootstrap"/>).
    /// </summary>
    public static class PreDuelSetupScreen
    {
        public sealed class Context
        {
            public ArDuelMatchConfig Match;
            public LocalAccountStore.Account Account;
            public CardDatabase Db;
            /// <summary>The deck a choice resolves to: -1 = the equipped deck (skip selection), else a deck box index.</summary>
            public Func<int, DeckFile> ResolveDeck;
            /// <summary>True for a deck built from the account's collection (ownership is checked); false for the
            /// launch's own file deck (starter / lab), which is checked for legality only.</summary>
            public Func<DeckFile, bool> IsAccountDeck;
            /// <summary>Label of the equipped deck row.</summary>
            public string EquippedName = "Equipped deck";
            public Action<ArDuelMatchConfig> OnStart;
            public Action OnCancel;
        }

        public const string CanvasName = "PreDuelSetupCanvas";

        /// <summary>One rendered line of design-size-13 body copy (<see cref="WrldzType.Readable"/> never shrinks it).</summary>
        static float Line13 => WrldzType.Readable(13) * 1.2f;

        /// <summary>Full-screen overlay in the duel scene (no MenuShell there), like the opponent sheet's overlay.</summary>
        public static GameObject OpenOverlay(Transform lifetimeHost, Context ctx)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();

            var canvas = WrldzTheme.Canvas(CanvasName, 95);
            EgyptianAgesAtmosphere.Attach(canvas, MenuAge.UmbraxRift, showAgeCaption: false);
            var host = canvas.gameObject;
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);

            var closed = false;
            void Close(bool cancelled)
            {
                if (closed) return;
                closed = true;
                if (cancelled) ctx?.OnCancel?.Invoke();
                if (host != null)
                    UnityEngine.Object.Destroy(host);
            }

            Build(canvas, ctx, () => Close(true), () => Close(false), UiPresentation.NonArPortrait);
            return host;
        }

        /// <summary>
        /// Build the sheet. <paramref name="onCancel"/> is the close chip (back to where the duel was launched from);
        /// <paramref name="onConfirmed"/> runs after <see cref="Context.OnStart"/> wrote the choices.
        /// </summary>
        public static RectTransform Build(Transform host, Context ctx, Action onCancel, Action onConfirmed = null,
            UiPresentation? force = null)
        {
            ctx ??= new Context();
            var match = ctx.Match ?? ArDuelMatchConfig.DefaultQuick();
            var acc = ctx.Account;
            acc?.EnsureProgress();
            acc?.EnsureInventory();
            var progress = acc?.progress;
            var inv = acc?.inventory;
            inv?.EnsureDeckBoxSlots();

            var vs = match.IsHumanOpponent ? "Player 1 vs Player 2"
                : "vs " + (string.IsNullOrEmpty(match.OpponentDisplayName) ? "the AI" : match.OpponentDisplayName);
            var frame = DualMenuPresenter.BuildFrame(host, "DUEL SETUP",
                vs + " · choose how turn 1 is decided, your deck and the format", onCancel, force);
            var body = frame.BodyHost;
            if (body == null) return frame.Root;

            // ---- state (seeded from the match, so a restart or a second look keeps the player's choices)
            var dictated = DuelFormatCatalog.DictatedBy(match);
            var formatId = dictated != null ? dictated.Id
                : DuelFormatCatalog.Get(match.RulesFormatId).Open ? DuelFormatCatalog.Get(match.RulesFormatId).Id
                : DuelFormatCatalog.StandardTcg;
            var bandId = string.IsNullOrEmpty(match.ErazBandId) ? ErazFormat.Original : match.ErazBandId;
            var deckIndex = match.PlayerDeckBoxIndex;
            var method = match.FirstPlayerMethod;
            // the coin's sides are dealt here, once, before the toss, and shown
            var headsIsP1 = UnityEngine.Random.value < 0.5f;
            var p1 = match.IsHumanOpponent ? "PLAYER 1" : "YOU";
            var p2 = match.IsHumanOpponent ? "PLAYER 2" : (string.IsNullOrEmpty(match.OpponentDisplayName) ? "OPPONENT" : match.OpponentDisplayName.ToUpperInvariant());
            var showEraz = ErazFormat.ShowsBadgeTray(match.FormatId);
            var checks = new List<PlayDeckValidator.CardCheck>();
            var valid = false;
            string reason = null;

            // ---- chrome: summary line, the list, START
            var summary = FloatingPanel.Body(body, "", 13);
            // two lines' worth: the summary wraps on a 16:9 frame and must not run into the list below it
            FloatingPanel.Place(summary.rectTransform, 0.02f, 0.885f, 0.98f, 0.985f);
            summary.alignment = TextAnchor.MiddleLeft;
            summary.color = DuelystUi.Cyan;
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            summary.verticalOverflow = VerticalWrapMode.Overflow;

            var scroll = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(body, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.01f, 0.15f, 0.99f, 0.875f);
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

            var status = FloatingPanel.Body(body, "", 13);
            FloatingPanel.Place(status.rectTransform, 0.02f, 0.095f, 0.98f, 0.15f);
            status.alignment = TextAnchor.MiddleLeft;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Overflow;

            Button start = null;
            void Confirm()
            {
                RunCheck();
                if (!valid)
                {
                    FreeUiKit.PlayClick();
                    status.text = "DECK NOT LEGAL · " + reason;
                    status.color = DuelystUi.Danger;
                    return;
                }

                FreeUiKit.PlayConfirm();
                DuelFormatCatalog.ApplyChoice(match, formatId);   // a dictated encounter keeps its own LP and overlay
                match.ErazBandId = bandId;
                match.PlayerDeckBoxIndex = deckIndex;
                match.FirstPlayerMethod = method;
                match.CoinHeadsIsPlayerOne = headsIsP1;
                match.FirstPlayerDecided = false;
                match.PreDuelSetupDone = true;
                Debug.Log($"[WRLDZ PreDuel] Setup confirmed · {DuelFormatCatalog.Get(match.RulesFormatId).Title} · " +
                          $"ERAZ {match.ErazBandId} · deck {(deckIndex < 0 ? "equipped" : "box " + deckIndex)} · " +
                          $"{method}{(method == FirstPlayerMethod.CoinToss ? $" (heads = {(headsIsP1 ? p1 : p2)})" : "")}");
                ctx.OnStart?.Invoke(match);
                onConfirmed?.Invoke();
            }

            start = HubChrome.Capsule(body, "START DUEL", Confirm, MenuCommandButton.Kind.Gold,
                centerTitle: true, titleSize: 20);
            FloatingPanel.Place(start.GetComponent<RectTransform>(), 0.25f, 0.015f, 0.75f, 0.095f);

            // ---- the deck check for the current choices
            DeckFile CurrentDeck() => ctx.ResolveDeck != null ? ctx.ResolveDeck(deckIndex) : null;

            void RunCheck()
            {
                var probe = new ArDuelMatchConfig
                {
                    Launch = match.Launch, Opponent = match.Opponent, FormatId = match.FormatId,
                    ErazBandId = bandId, RulesFormatId = formatId
                };
                var deck = CurrentDeck();
                var owned = deck != null && (ctx.IsAccountDeck == null || ctx.IsAccountDeck(deck));
                valid = PlayDeckValidator.Inspect(deck, acc, ctx.Db, probe, checks, out reason, ownership: owned);
                if (deck == null) reason ??= "no deck";
            }

            string DeckLabel()
            {
                if (deckIndex < 0) return ctx.EquippedName;
                var b = inv?.deckBoxes != null && deckIndex < inv.deckBoxes.Length ? inv.deckBoxes[deckIndex] : null;
                return b == null ? "Deck " + (deckIndex + 1) : (string.IsNullOrEmpty(b.name) ? "Deck " + (deckIndex + 1) : b.name);
            }

            void Rebuild()
            {
                RunCheck();
                var f = DuelFormatCatalog.Get(formatId);
                summary.text = $"{f.Title.ToUpperInvariant()} · {f.LpLine} · ERAZ {BandName(bandId)} · {DeckLabel()} · " +
                               (method == FirstPlayerMethod.CoinToss ? "coin toss" : "dice roll");
                status.text = valid ? "Deck legal for this format." : "DECK NOT LEGAL · " + reason;
                status.color = valid ? DuelystUi.TextMuted : DuelystUi.Danger;
                if (start != null) start.interactable = valid;

                // rows are rebuilt from a row's own click: hide now, destroy after the callback (DestroyImmediate
                // from inside a Button onClick fatals the Editor); inactive rows drop out of the layout at once
                for (var i = content.transform.childCount - 1; i >= 0; i--)
                    FloatingPanel.DestroyDeferred(content.transform.GetChild(i).gameObject);
                var t = content.transform;

                // FIRST TURN
                Header(t, "FIRST TURN");
                Row(t, "COIN TOSS", $"{p1}: {(headsIsP1 ? "HEADS" : "TAILS")}  ·  {p2}: {(headsIsP1 ? "TAILS" : "HEADS")}  ·  the coin decides",
                    method == FirstPlayerMethod.CoinToss ? "SELECTED" : null, false,
                    () => { method = FirstPlayerMethod.CoinToss; FreeUiKit.PlayClick(); Rebuild(); });
                Row(t, "DICE ROLL", "Both roll a die · the higher roll takes turn 1 · a tie is rolled again",
                    method == FirstPlayerMethod.DiceRoll ? "SELECTED" : null, false,
                    () => { method = FirstPlayerMethod.DiceRoll; FreeUiKit.PlayClick(); Rebuild(); });

                // DECK
                Header(t, "DECK");
                Row(t, ctx.EquippedName.ToUpperInvariant(), "Skip deck selection · play the deck you have equipped",
                    deckIndex < 0 ? "SELECTED" : null, false,
                    () => { deckIndex = -1; FreeUiKit.PlayClick(); Rebuild(); });
                if (inv?.deckBoxes != null)
                {
                    for (var i = 0; i < inv.deckBoxes.Length; i++)
                    {
                        var box = inv.deckBoxes[i];
                        if (box == null || (box.main?.Length ?? 0) == 0) continue;   // an empty box is not a deck
                        var idx = i;
                        var n = (box.main?.Length ?? 0);
                        var nm = string.IsNullOrEmpty(box.name) ? "Deck " + (i + 1) : box.name;
                        Row(t, nm.ToUpperInvariant(), $"{n} cards main · {box.extra?.Length ?? 0} extra · {box.side?.Length ?? 0} side",
                            deckIndex == idx ? "SELECTED" : null, false,
                            () => { deckIndex = idx; FreeUiKit.PlayClick(); Rebuild(); });
                    }
                }

                // DECK CHECK — every card, the illegal ones greyed with the reason; nothing is removed
                var illegal = 0;
                foreach (var c in checks) if (!c.Legal) illegal++;
                Header(t, $"DECK CHECK · {f.Title.ToUpperInvariant()}" + (illegal > 0 ? $" · {illegal} NOT LEGAL" : " · OK"));
                if (checks.Count == 0)
                    Row(t, "No cards", reason ?? "", null, true, null);
                foreach (var c in checks)
                    CardRow(t, c);

                // ERAZ sub-format
                if (showEraz)
                {
                    Header(t, "ERAZ · ERA");
                    foreach (var id in ErazFormat.BandIdsInOrder())
                    {
                        // the badge gates the era; an era whose pool is not fully compiled yet is still playable, the
                        // deck wall keeps it to lab / starter cards (ErazDeckRules), so it is listed, not locked
                        var released = ErazFormat.IsReleased(id);
                        var open = (progress != null && ErazProgress.CanSelectBand(progress, id)) || bandId == id;
                        var detail = !open ? "LOCKED · badge required"
                            : released ? "Card pool and end-of-era list of this era"
                            : "Era not released · lab and starter cards only";
                        var bid = id;
                        Row(t, BandName(id).ToUpperInvariant(), detail,
                            open ? (bandId == id ? "SELECTED" : null) : "LOCKED", !open,
                            open ? () => { bandId = bid; FreeUiKit.PlayClick(); Rebuild(); } : null);
                    }
                }

                // FORMAT
                Header(t, dictated != null ? "FORMAT · SET BY THIS ENCOUNTER" : "FORMAT");
                foreach (var def in DuelFormatCatalog.All)
                {
                    var isCurrent = string.Equals(def.Id, formatId, StringComparison.OrdinalIgnoreCase);
                    if (dictated != null)
                    {
                        // the encounter's own LP (a 4000 LP street duel, a boss's 10,000), not the catalog line
                        var lpLine = isCurrent && match.StartingLp > 0 ? match.StartingLp + " LP" : def.LpLine;
                        Row(t, def.Title.ToUpperInvariant(), def.Rules + "\n" + lpLine,
                            isCurrent ? "SELECTED" : "LOCKED", !isCurrent, null);
                        continue;
                    }

                    var owned = def.BadgeId != null && FormatProgress.HasBadge(progress, def.BadgeId);
                    var lockText = def.Open ? null : owned ? "LOCKED · badge owned · rules not final" : "LOCKED · " + def.LockReason;
                    var d = def;
                    Row(t, def.Title.ToUpperInvariant(), (def.Open ? def.Rules : lockText) + "\n" + def.LpLine,
                        def.Open ? (isCurrent ? "SELECTED" : null) : "LOCKED", !def.Open,
                        def.Open ? () => { formatId = d.Id; FreeUiKit.PlayClick(); Rebuild(); } : null);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
            }

            Rebuild();
            return frame.Root;
        }

        static string BandName(string id)
        {
            var b = ErazFormat.Band(id);
            return b != null && !string.IsNullOrEmpty(b.name) ? b.name : id;
        }

        /// <summary>A section title over its rows (not a button).</summary>
        static void Header(Transform host, string text)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = Line13 + 10f;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.10f, 0.14f, 0.24f, 0.95f);
            img.raycastTarget = false;
            var t = FloatingPanel.Body(go.transform, text, 13);
            FloatingPanel.Place(t.rectTransform, 0.03f, 0.05f, 0.97f, 0.95f);
            t.alignment = TextAnchor.MiddleLeft;
            t.fontStyle = FontStyle.Bold;
            t.color = DuelystUi.Cyan;
        }

        /// <summary>
        /// One option row: title, a line of detail, and a tag on the right (SELECTED in gold, LOCKED in the danger
        /// colour). A locked row is listed but not interactive (the project's locked-option convention).
        /// </summary>
        static void Row(Transform host, string title, string detail, string tag, bool locked, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(host, false);
            // the title line plus every line of detail (format rows carry rules + LP) plus breathing room, whatever
            // the type scale — sized for two lines, the third line was cut off
            var lines = 1 + (string.IsNullOrEmpty(detail) ? 1 : detail.Split('\n').Length);
            go.GetComponent<LayoutElement>().minHeight = Line13 * lines + 14f;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            var selected = tag == "SELECTED";
            img.color = selected ? new Color(0.22f, 0.18f, 0.06f, 0.95f)
                : locked ? new Color(0.04f, 0.05f, 0.08f, 0.75f)
                : new Color(0.05f, 0.08f, 0.14f, 0.9f);
            var t = FloatingPanel.Body(go.transform, "    " + title + "\n    " + (detail ?? ""), 13);
            FloatingPanel.Place(t.rectTransform, 0.02f, 0.04f, 0.78f, 0.96f);
            t.alignment = TextAnchor.MiddleLeft;
            t.color = locked ? DuelystUi.TextMuted : DuelystUi.TextCream;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (!string.IsNullOrEmpty(tag))
            {
                var tt = FloatingPanel.Body(go.transform, tag, 13);
                FloatingPanel.Place(tt.rectTransform, 0.78f, 0.04f, 0.985f, 0.96f);
                tt.alignment = TextAnchor.MiddleRight;
                tt.fontStyle = FontStyle.Bold;
                tt.verticalOverflow = VerticalWrapMode.Overflow;
                tt.color = selected ? DuelystUi.GoldHot : locked ? DuelystUi.Danger : DuelystUi.TextMuted;
            }

            var btn = go.GetComponent<Button>();
            if (onClick != null && !locked)
                btn.onClick.AddListener(() => onClick());
            else
                btn.interactable = false;
        }

        /// <summary>One deck card: name × copies; greyed out with the reason when the format refuses it.</summary>
        static void CardRow(Transform host, PlayDeckValidator.CardCheck c)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = Line13 + 6f;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = c.Legal ? new Color(0.05f, 0.08f, 0.14f, 0.6f) : new Color(0.08f, 0.03f, 0.04f, 0.7f);
            img.raycastTarget = false;
            var t = FloatingPanel.Body(go.transform, $"    {c.Name}  ×{c.Qty}", 13);
            FloatingPanel.Place(t.rectTransform, 0.02f, 0.02f, 0.58f, 0.98f);
            t.alignment = TextAnchor.MiddleLeft;
            t.color = c.Legal ? DuelystUi.TextCream : new Color(0.55f, 0.55f, 0.6f, 0.85f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (!c.Legal)
            {
                var r = FloatingPanel.Body(go.transform, "✕ " + (c.Reason ?? "not legal"), 13);
                FloatingPanel.Place(r.rectTransform, 0.58f, 0.02f, 0.98f, 0.98f);
                r.alignment = TextAnchor.MiddleRight;
                r.color = DuelystUi.Danger;
                r.horizontalOverflow = HorizontalWrapMode.Wrap;
                r.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }
    }
}
