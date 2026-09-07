using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Deck builder: construction board (Main/Extra/Side always visible) +
    /// searchable card list. Home copies greyed. Phone edits; AR holo is read-only.
    /// </summary>
    public static partial class DeckCollectionScreen
    {
        /// <summary>Actions = hold-menu; Editor = builder. Select is unused (switcher replaces list).</summary>
        enum Phase { Select = 0, Actions, Editor }
        enum Section { Main = 0, Extra, Side }
        enum Filter { All = 0, Mon, Spell, Trap, Extra }
        enum KindFilter
        {
            Any = 0, Normal, Effect, Fusion, Ritual, Flip, Spirit, Union, Toon,
            Tuner, Synchro, Xyz, Link, Pendulum
        }
        enum SortMode { Name = 0, Level, Atk, Def, Type }
        enum FilterScope { Both = 0, Collection, Deck }
        enum StatBand { Any = 0, Lt1000, R1000, R1500, R2000, Ge2500 }
        enum InDeckFilter { Any = 0, InDeck, NotInDeck }

        const float LongPressSec = 0.45f;
        /// <summary>
        /// How many collection chips to spawn per scroll page. The full match list
        /// is kept in memory; more rows append when the player nears the bottom.
        /// </summary>
        const int PoolPageSize = 72;

        // Art-first chips — enough per row to scan, not a catalog of nameplates
        const int CardsPerRow = 5;
        const float ChipW = 100f;
        const float ChipH = 144f;
        const float ChipGap = 5f;
        const float GridScrollSensitivity = 70f;
        /// <summary>Normalized X gap between collection (left) and deck (right).</summary>
        const float SplitGutter = 0.045f;

        // Tall name row: compact overlays are ~400px. A 6% row is ~24px — 9-slice
        // HudChip borders (16+16) collapse the text rect to 0 and Unity draws no glyphs.
        const float ChromeNameY0 = 0.88f;
        const float ChromeNameY1 = 1f;
        const float ChromeToolY0 = 0.848f;
        const float ChromeToolY1 = 0.900f;
        const float SearchY0 = 0.848f;
        const float SearchY1 = 0.900f;
        const float ScopeY0 = 0.684f;
        const float ScopeY1 = 0.726f;
        const float FilterIcoY0 = 0.848f;
        const float FilterIcoY1 = 0.900f;
        const float ChipY0 = 0.584f;
        const float ChipY1 = 0.620f;
        const float GridTopChips = 0.578f;
        const float GridTopClear = 0.626f;
        const float NamePillX1 = 0.86f;
        const float SwitcherX1 = 0.50f;
        const float BoardY0 = 0.400f;
        const float BoardY1 = 0.840f;
        const float ListY0 = 0.000f;
        const float ListY1 = 0.368f;

        static readonly string[] AttributeOptions =
            { "DARK", "LIGHT", "EARTH", "WATER", "FIRE", "WIND", "DIVINE" };
        static readonly string[] AttributeAbbr =
            { "DARK", "LGHT", "ERTH", "WTR", "FIRE", "WIND", "DIV" };
        static readonly string[] MonsterTypeOptions =
        {
            "Warrior", "Spellcaster", "Fiend", "Dragon", "Machine", "Beast",
            "Winged Beast", "Beast-Warrior", "Fairy", "Zombie", "Rock", "Insect",
            "Aqua", "Pyro", "Thunder", "Reptile", "Fish", "Dinosaur", "Plant",
            "Sea Serpent", "Psychic", "Wyrm", "Cyberse", "Divine-Beast",
            "Creator God", "Illusion"
        };
        static readonly string[] MonsterTypeAbbr =
        {
            "WAR", "SPC", "FND", "DRG", "MCH", "BST",
            "W.BST", "B.WAR", "FRY", "ZMB", "RCK", "INS",
            "AQA", "PYR", "THN", "RPT", "FSH", "DNO", "PLT",
            "SEA", "PSY", "WYR", "CYB", "DIVB",
            "CRTR", "ILL"
        };
        static readonly (KindFilter Kind, string Lab)[] KindOptions =
        {
            (KindFilter.Any, "ANY"), (KindFilter.Normal, "NRM"), (KindFilter.Effect, "EFF"),
            (KindFilter.Fusion, "FUS"), (KindFilter.Ritual, "RIT"), (KindFilter.Flip, "FLP"),
            (KindFilter.Spirit, "SPR"), (KindFilter.Union, "UNI"), (KindFilter.Toon, "TOON"),
            (KindFilter.Tuner, "TUN"), (KindFilter.Synchro, "SYN"), (KindFilter.Xyz, "XYZ"),
            (KindFilter.Link, "LNK"), (KindFilter.Pendulum, "PND")
        };
        static readonly string[] SpellKindOptions =
            { "Normal", "Continuous", "Equip", "Field", "Quick-Play", "Ritual" };
        static readonly string[] SpellKindAbbr =
            { "NRM", "CONT", "EQ", "FLD", "QP", "RIT" };
        static readonly string[] TrapKindOptions =
            { "Normal", "Continuous", "Counter" };
        static readonly string[] TrapKindAbbr =
            { "NRM", "CONT", "CTR" };
        static readonly (StatBand Band, string Lab)[] StatBandOptions =
        {
            (StatBand.Any, "ANY"), (StatBand.Lt1000, "<1K"), (StatBand.R1000, "1K"),
            (StatBand.R1500, "1.5K"), (StatBand.R2000, "2K"), (StatBand.Ge2500, "2.5K+")
        };

        class State
        {
            public Phase Phase = Phase.Select;
            public int DeckIndex;
            public Section Section = Section.Main;
            public Filter Filter = Filter.All;
            public KindFilter Kind = KindFilter.Any;
            public FilterScope Scope = FilterScope.Both;
            public int SelectedId;
            public string Search = "";
            public string Archetype = "";
            public string MonsterType = "";
            public string Attribute = "";
            /// <summary>0 = any. 1–8 exact. 9 = level/rank 9+.</summary>
            public int Level = 0;
            public string SpellKind = "";
            public string TrapKind = "";
            public StatBand AtkBand = StatBand.Any;
            public StatBand DefBand = StatBand.Any;
            public InDeckFilter InDeck = InDeckFilter.Any;
            public SortMode Sort = SortMode.Name;
            public List<int> Main = new();
            public List<int> Extra = new();
            public List<int> Side = new();
            public List<int> RevertMain = new();
            public List<int> RevertExtra = new();
            public List<int> RevertSide = new();
            public bool RevertArmed;
            public int RevertDeckIndex = -1;
            public CardDatabase Db;
            public PlayerInventory Inv;
            public LocalAccountStore.Account Acc;
            public UiPresentation Presentation;
            public RectTransform Window;
            public RectTransform Body;
            public Text Title;
            public Text Status;
            public Text ModeBadge;
            public GameObject Inspect;
            public Image InspectArt;
            public Text InspectName;
            public Text InspectStats;
            public Text InspectDesc;
            public Button InspectAdd;
            public Button InspectRem;
            public Action OnClose;
            public Action Rebuild;
            public Action RefreshCards;
            public Transform PoolGrid;
            public Transform DeckGrid;
            public ScrollRect PoolScroll;
            public ScrollRect DeckScroll;
            public Text PoolHeader;
            public Text DeckHeader;
            public Text MatchLine;
            public RectTransform BoardHost;
            public RectTransform MainTray;
            public RectTransform ExtraTray;
            public RectTransform SideTray;
            public Transform MainSlots;
            public Transform ExtraSlots;
            public Transform SideSlots;
            public ScrollRect MainScroll;
            public ScrollRect ExtraScroll;
            public ScrollRect SideScroll;
            public Text MainCountLab;
            public Text ExtraCountLab;
            public Text SideCountLab;
            public Image MainMeter;
            public Section DragSection;
            public List<PoolRow> PoolRows;
            public int PoolVisible;
            public bool PoolAppending;
            public Transform DragLayer;
            public GameObject Ghost;
            public int DragId;
            public bool DragFromPool;
            /// <summary>Upward deck-switch dropdown host (destroyed on rebuild).</summary>
            public GameObject DeckDropHost;
            public bool DeckDropOpen;
            public GameObject DeckMenuHost;
            public GameObject DeckRenameHost;
            public InputField NameField;
        }

        public static RectTransform Build(Transform modalHost, Action onClose, UiPresentation? force = null)
        {
            FreeUiKit.EnsureLoaded();

            // Same deck UI for AR holo + non-AR phone; size/opacity from Settings (MenuChromePrefs).
            var presentation = force
                               ?? ScreenRouter.Instance?.Presentation
                               ?? DualMenuPresenter.ResolveDefaultPresentation();

            // Open menu: world/map shows through. Dim only catches “click outside card”.
            var root = new GameObject("DeckMenuRoot", typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            root.SetParent(modalHost, false);
            FloatingPanel.Stretch(root);
            var dim = root.GetComponent<Image>();
            dim.sprite = UiFoundation.WhiteSprite();
            dim.color = new Color(0.02f, 0.03f, 0.07f, 0.78f);
            dim.raycastTarget = true;

            var win = new GameObject("DeckMenuWindow", typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            win.SetParent(root, false);
            FloatingPanel.Place(win, 0.0f, 0.0f, 1f, 1f);
            var wImg = win.GetComponent<Image>();
            var deckBg = ImagineAssets.BgDeckBuilder();
            if (deckBg != null)
            {
                wImg.sprite = deckBg;
                wImg.color = new Color(1f, 1f, 1f, 0.62f);
                wImg.preserveAspect = false;
            }
            else
            {
                wImg.sprite = UiFoundation.WhiteSprite();
                wImg.color = new Color(0.04f, 0.06f, 0.10f, 0.06f);
            }
            wImg.raycastTarget = true;
            var winBtn = win.gameObject.AddComponent<Button>();
            winBtn.targetGraphic = wImg;
            winBtn.transition = Selectable.Transition.None;

            var body = win;

            var st = new State
            {
                Db = CardDatabase.Load(),
                Acc = AppSession.Ensure().Account,
                Presentation = presentation,
                Window = win,
                Body = body,
                // Jump straight into builder — deck switcher replaces the old list screen
                Phase = Phase.Editor
            };

            if (st.Acc != null &&
                (LabAdminService.IsAdmin(st.Acc) || Application.isEditor ||
                 LabCatalogService.IsGranted(st.Acc)))
            {
                var firstAdmin = st.Acc.progress == null || !st.Acc.progress.labAdmin;
                LabAdminService.MaxOut(st.Acc);
                if (firstAdmin)
                    ProgressionService.Persist(st.Acc);
            }

            st.Acc?.EnsureInventory();
            st.Inv = st.Acc?.inventory;
            st.Inv?.EnsureValid();
            if (st.Inv != null && LabCatalogService.IsGranted(st.Acc))
                LabCatalogService.KeepCatalogOnHand(st.Inv);
            st.Inv?.EnsureDeckBoxSlots();
            if (st.Inv != null)
            {
                var active = st.Inv.ActivePlayDeckIndex;
                if (active >= 0)
                    st.DeckIndex = active;
                else if (st.Inv.deckBoxes != null)
                {
                    for (var i = 0; i < st.Inv.deckBoxes.Length; i++)
                    {
                        var b = st.Inv.deckBoxes[i];
                        if (b != null && (b.occupied || (b.main?.Length ?? 0) > 0))
                        {
                            st.DeckIndex = i;
                            break;
                        }
                    }
                }
            }

            st.OnClose = () =>
            {
                onClose?.Invoke();
                if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            };

            var dimBtn = root.gameObject.GetComponent<Button>() ?? root.gameObject.AddComponent<Button>();
            dimBtn.targetGraphic = dim;
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(() =>
            {
                if (st.Inspect != null && st.Inspect.activeSelf)
                    HideInspect(st);
            });
            winBtn.onClick.AddListener(() =>
            {
                if (st.Inspect != null && st.Inspect.activeSelf)
                    HideInspect(st);
            });

            st.Status = L(body, "", 11, new Color(0.85f, 0.9f, 0.95f, 0.9f), TextAnchor.MiddleLeft);
            FloatingPanel.Place(st.Status.rectTransform, 0.03f, 0.006f, 0.97f, 0.046f);

            // Drag layer: only captures during drag (never blocks list clicks / never paints)
            var drag = new GameObject("DragLayer", typeof(RectTransform), typeof(CanvasGroup));
            drag.transform.SetParent(body, false);
            FloatingPanel.Stretch(drag.GetComponent<RectTransform>());
            var dragCg = drag.GetComponent<CanvasGroup>();
            dragCg.blocksRaycasts = false;
            dragCg.interactable = false;
            st.DragLayer = drag.transform;

            BuildInspect(body, st);

            st.Rebuild = () =>
            {
                // DestroyImmediate-safe: rename so a same-frame Find can't reattach to a dying node
                var old = body.Find("PhaseBody");
                if (old != null)
                {
                    old.name = "PhaseBody_dead";
                    FloatingPanel.DestroyNow(old.gameObject);
                }

                var phase = new GameObject("PhaseBody", typeof(RectTransform), typeof(Image))
                    .GetComponent<RectTransform>();
                phase.SetParent(body, false);
                FloatingPanel.Place(phase, 0.016f, 0.050f, 0.984f, 0.988f);
                // Light phase plate — transparent chrome; chips/rows carry their own contrast
                var phaseImg = phase.GetComponent<Image>();
                phaseImg.sprite = UiFoundation.WhiteSprite();
                phaseImg.color = new Color(0.03f, 0.04f, 0.08f, 0.10f);
                phaseImg.raycastTarget = false;

                // Keep inventory live
                st.Acc = AppSession.Ensure().Account ?? st.Acc;
                st.Acc?.EnsureInventory();
                st.Inv = st.Acc?.inventory ?? st.Inv;
                st.Inv?.EnsureValid();
                st.Inv?.EnsureDeckBoxSlots();

                var deckName = DeckName(st);
                st.DeckDropOpen = false;
                st.DeckDropHost = null;
                st.DeckMenuHost = null;
                st.DeckRenameHost = null;
                st.NameField = null;
                switch (st.Phase)
                {
                    case Phase.Select:
                        // Legacy entry → editor with switcher
                        st.Phase = Phase.Editor;
                        if (st.Title != null) st.Title.text = "DECK";
                        Status(st, "Tap name to rename · caret to switch · hold a deck for options", true);
                        LoadDeck(st);
                        BuildEditor(phase, st);
                        break;
                    case Phase.Actions:
                        if (st.Title != null) st.Title.text = "DECK";
                        Status(st, "Edit · rename · clear · hold again or BACK", true);
                        BuildActions(phase, st);
                        break;
                    case Phase.Editor:
                        if (st.Title != null) st.Title.text = "DECK";
                        Status(st, "Tap the deck name to rename · caret to switch", true);
                        LoadDeck(st);
                        BuildEditor(phase, st);
                        break;
                }
                _ = deckName;

                // Phase content draws above drag layer; inspect stays on top when open
                phase.SetAsLastSibling();
                st.DragLayer.SetAsLastSibling();
                if (st.Inspect != null) st.Inspect.transform.SetAsLastSibling();
                Canvas.ForceUpdateCanvases();
            };
            st.Rebuild();
            return root;
        }

        static string DeckName(State st)
        {
            if (st?.Inv?.deckBoxes == null || st.Inv.deckBoxes.Length == 0) return "Deck";
            var i = ClampDeck(st);
            var box = st.Inv.deckBoxes[i];
            return string.IsNullOrEmpty(box?.name) ? "Deck " + (i + 1) : box.name;
        }

        // ── Select: absolute-layout list (no ScrollRect / CSF — those were zero-height clipping) ──

        static void BuildSelect(RectTransform phase, State st)
        {
            if (st.Inv == null)
            {
                st.Acc?.EnsureInventory();
                st.Inv = st.Acc?.inventory;
            }

            st.Inv?.EnsureDeckBoxSlots();
            var boxes = st.Inv?.deckBoxes;
            var n = boxes?.Length ?? 0;

            Debug.Log($"[WRLDZ DeckUI] Select list · boxes={n} · inv={(st.Inv != null)} · acc={st.Acc?.username}");

            // Header always visible (proves phase plate is drawing)
            var head = Label(phase, n > 0 ? $"YOUR DECKS  ({n})" : "YOUR DECKS", 16, DuelystUi.GoldHot,
                TextAnchor.MiddleLeft);
            FloatingPanel.Place(head.rectTransform, 0.04f, 0.90f, 0.96f, 0.98f);

            if (n == 0)
            {
                var empty = Label(phase, "No deck boxes yet.\nTap + NEW DECK below.", 14, Color.white,
                    TextAnchor.MiddleCenter);
                FloatingPanel.Place(empty.rectTransform, 0.06f, 0.40f, 0.94f, 0.70f);
                Status(st, "No decks — create one", false);
            }
            else
            {
                // Stack rows with explicit anchors (independent of LayoutGroup / ContentSizeFitter)
                const float top = 0.88f;
                const float rowH = 0.095f;
                const float gap = 0.012f;
                var maxVisible = 7;
                var shown = Mathf.Min(n, maxVisible);
                for (var i = 0; i < shown; i++)
                {
                    var idx = i;
                    var box = boxes[i];
                    var name = string.IsNullOrEmpty(box?.name) ? "Deck " + (i + 1) : box.name;
                    var m = box?.main?.Length ?? 0;
                    var ready = m >= TcgRules.MainDeckMin;
                    var y1 = top - i * (rowH + gap);
                    var y0 = y1 - rowH;
                    PlaceDeckRow(phase, 0.03f, y0, 0.97f, y1, name,
                        m + " main · " + (ready ? "ready" : "build"), ready, () =>
                        {
                            st.DeckIndex = idx;
                            st.Phase = Phase.Actions;
                            FreeUiKit.PlaySelect();
                            st.Rebuild();
                        });
                    Debug.Log($"[WRLDZ DeckUI]   row[{i}] '{name}' main={m}");
                }

                if (n > maxVisible)
                {
                    var more = Label(phase, $"+ {n - maxVisible} more boxes", 12, DuelystUi.TextMuted,
                        TextAnchor.MiddleCenter);
                    FloatingPanel.Place(more.rectTransform, 0.1f, 0.12f, 0.9f, 0.18f);
                }

                Status(st, $"Tap a deck · {n} box(es) · main ≥ {TcgRules.MainDeckMin} to duel", true);
            }

            var add = SolidButton(phase, "+ NEW DECK", true, () =>
            {
                if (st.Inv == null)
                {
                    Status(st, "No inventory.", false);
                    return;
                }

                if (!InventoryService.TryCreateDeckBox(st.Acc, out _, out var err))
                {
                    Status(st, err ?? "Max deck boxes.", false);
                    return;
                }

                Save(st);
                FreeUiKit.PlayConfirm();
                st.Rebuild();
            });
            FloatingPanel.Place(add.GetComponent<RectTransform>(), 0.18f, 0.01f, 0.82f, 0.10f);
        }

        /// <summary>
        /// Fully specified deck row: anchors set by caller, solid fill, BuiltinFont text.
        /// No LayoutGroup, no 9-slice plates, no ContentSizeFitter.
        /// </summary>
        static void PlaceDeckRow(Transform parent, float x0, float y0, float x1, float y1,
            string title, string meta, bool highlight, Action onClick)
        {
            var go = new GameObject("DeckRow", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            FloatingPanel.Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);

            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = highlight
                ? new Color(0.05f, 0.32f, 0.42f, 1f)
                : new Color(0.10f, 0.12f, 0.18f, 1f);
            img.raycastTarget = true;

            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(go.transform, false);
            FloatingPanel.Place(bar.GetComponent<RectTransform>(), 0f, 0.15f, 0.025f, 0.85f);
            var bImg = bar.GetComponent<Image>();
            bImg.sprite = UiFoundation.WhiteSprite();
            bImg.color = highlight ? new Color(0.3f, 0.95f, 1f, 1f) : DuelystUi.GoldHot;
            bImg.raycastTarget = false;

            var titleT = Label(go.transform, title ?? "Deck", 15, Color.white, TextAnchor.MiddleLeft);
            FloatingPanel.Place(titleT.rectTransform, 0.05f, 0.1f, 0.58f, 0.9f);
            titleT.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleT.verticalOverflow = VerticalWrapMode.Overflow;
            titleT.resizeTextForBestFit = true;
            titleT.resizeTextMinSize = 12;
            titleT.resizeTextMaxSize = 28;

            var metaT = Label(go.transform, meta ?? "", 12,
                highlight ? new Color(0.6f, 1f, 0.85f, 1f) : new Color(0.85f, 0.9f, 0.95f, 1f),
                TextAnchor.MiddleRight);
            FloatingPanel.Place(metaT.rectTransform, 0.58f, 0.1f, 0.88f, 0.9f);
            metaT.horizontalOverflow = HorizontalWrapMode.Overflow;

            var chev = Label(go.transform, ">", 18, DuelystUi.GoldHot, TextAnchor.MiddleCenter);
            FloatingPanel.Place(chev.rectTransform, 0.88f, 0.1f, 0.98f, 0.9f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            var cols = btn.colors;
            cols.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cols.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            btn.colors = cols;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                onClick?.Invoke();
            });
        }

        /// <summary>Solid CTA — no 9-slice plates (those break at toolbar size).</summary>
        static Button SolidButton(Transform parent, string label, bool gold, Action onClick) =>
            DeckChromeButton(parent, label, onClick,
                gold ? new Color(0.12f, 0.42f, 0.52f, 0.50f) : new Color(0.10f, 0.32f, 0.44f, 0.45f),
                compact: false);

        static Button DeckChromeButton(Transform parent, string label, Action onClick, Color fill,
            bool compact)
        {
            var go = new GameObject(string.IsNullOrEmpty(label) ? "DeckBtn" : "DeckBtn_" + label,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = fill;
            img.raycastTarget = true;

            var t = Label(go.transform, label ?? "", compact ? 13 : 14, Color.white,
                TextAnchor.MiddleCenter);
            FloatingPanel.Stretch(t.rectTransform, compact ? 3f : 5f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = compact ? 11 : 12;
            t.resizeTextMaxSize = compact ? 16 : 20;
            t.alignment = TextAnchor.MiddleCenter;

            var le = go.GetComponent<LayoutElement>();
            le.minHeight = compact ? 34f : 44f;
            le.preferredHeight = le.minHeight;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var block = ColorBlock.defaultColorBlock;
            block.normalColor = Color.white;
            block.highlightedColor = new Color(1.12f, 1.12f, 1.14f, 1f);
            block.pressedColor = new Color(0.75f, 0.78f, 0.82f, 1f);
            block.fadeDuration = 0.08f;
            btn.colors = block;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                onClick?.Invoke();
            });
            return btn;
        }

        /// <summary>
        /// Text that always has a font (Builtin fallback) and heavy outline — never blank glyphs.
        /// </summary>
        static Text Label(Transform parent, string text, int designSize, Color color, TextAnchor align)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            // Prefer project body font, but never leave font null (blank UI text)
            var font = WrldzType.Body() ?? UiFoundation.BuiltinFont();
            t.font = font;
            t.fontSize = Mathf.Max(18, WrldzType.Readable(designSize));
            t.fontStyle = FontStyle.Bold;
            t.text = text ?? "";
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = false;
            // Outline so text survives dark + light plates
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 1f);
            o.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        static readonly Color GlassQuiet = new(0.08f, 0.12f, 0.18f, 0.94f);
        static readonly Color GlassCyan = new(0.10f, 0.42f, 0.52f, 0.94f);
        static readonly Color GlassGold = new(0.32f, 0.24f, 0.06f, 0.94f);
        static readonly Color GlassRose = new(0.48f, 0.10f, 0.16f, 0.94f);
        static readonly Color EdgeGold = new(0.95f, 0.82f, 0.32f, 1f);
        static readonly Color GlassOn = new(0.16f, 0.55f, 0.70f, 0.28f);
        static readonly Color EdgeCyan = new(0.35f, 0.82f, 0.98f, 0.42f);
        static readonly Color EdgeRose = new(1f, 0.38f, 0.44f, 0.40f);

        static void StyleGlass(Image img, Color fill, Color edge)
        {
            if (img == null) return;
            img.sprite = UiTheme.RoundedRectSprite() ?? UiFoundation.WhiteSprite();
            img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced : Image.Type.Simple;
            img.color = fill;
            var ol = img.GetComponent<Outline>() ?? img.gameObject.AddComponent<Outline>();
            ol.effectColor = edge;
            ol.effectDistance = new Vector2(1.5f, -1.5f);
            ol.useGraphicAlpha = false;
        }

        static void StyleChromeType(Text t, int designSize, TextAnchor align, Color color)
        {
            if (t == null) return;
            t.font = FreeUiKit.UiFont() ?? WrldzType.Body() ?? UiFoundation.BuiltinFont();
            t.fontStyle = FontStyle.Bold;
            t.fontSize = WrldzType.Readable(designSize);
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignment = align;
            t.alignByGeometry = true;
            t.color = color;
            t.raycastTarget = false;
        }

        static Button GlassAction(Transform parent, string label, Color fill, Color edge, Action onClick)
        {
            var b = DeckChromeButton(parent, label, onClick, fill, compact: false);
            StyleGlass(b.GetComponent<Image>(), fill, edge);
            var t = b.GetComponentInChildren<Text>();
            WrldzType.StyleButtonLabel(t, 18, display: false);
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return b;
        }

        /// <summary>Pin a toolbar control to the top of the sheet at a fixed pixel height.</summary>
        static void PinTop(RectTransform rt, float x0, float x1, float fromTop, float height)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(x0, 1f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -fromTop);
        }

        /// <summary>Stretch a panel from a pixel offset below the top down to a normalized bottom.</summary>
        static void PinBelow(RectTransform rt, float x0, float x1, float fromTop, float y0)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, -fromTop);
        }

        static Button PlateAction(Transform parent, string label, Sprite plate, Action onClick)
        {
            var b = DeckChromeButton(parent, label, onClick, Color.white, compact: false);
            var img = b.GetComponent<Image>();
            if (plate != null)
            {
                img.sprite = plate;
                img.type = plate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                img.color = Color.white;
                var ol = img.GetComponent<Outline>();
                if (ol != null) ol.enabled = false;
            }
            StyleChromeType(b.GetComponentInChildren<Text>(), 16, TextAnchor.MiddleCenter,
                DuelystUi.TextCream);
            return b;
        }

        // ── Actions submenu ──────────────────────────────────────────────────

        static void BuildActions(RectTransform phase, State st)
        {
            st.Inv?.EnsureDeckBoxSlots();
            if (st.Inv?.deckBoxes == null || st.Inv.deckBoxes.Length == 0)
            {
                var miss = Label(phase, "No deck selected.", 14, Color.white, TextAnchor.MiddleCenter);
                FloatingPanel.Place(miss.rectTransform, 0.1f, 0.4f, 0.9f, 0.6f);
                return;
            }

            var box = st.Inv.deckBoxes[ClampDeck(st)];
            var name = box?.name ?? "Deck";
            var m = box?.main?.Length ?? 0;

            var head = Label(phase, name, 16, DuelystUi.GoldHot, TextAnchor.MiddleCenter);
            FloatingPanel.Place(head.rectTransform, 0.05f, 0.88f, 0.84f, 0.96f);
            var close = Btn(phase, "×", () =>
            {
                FreeUiKit.PlayClick();
                RequestLeaveEditor(st);
            }, compact: true);
            FloatingPanel.Place(close.GetComponent<RectTransform>(), 0.86f, 0.88f, 0.98f, 0.96f);

            var legal = m >= TcgRules.MainDeckMin && m <= TcgRules.MainDeckMax;
            var sub = Label(phase,
                $"Main {m}/{TcgRules.MainDeckMax} · Ex {box?.extra?.Length ?? 0} · Side {box?.side?.Length ?? 0}" +
                (legal ? " · duel-ready" : " · need " + TcgRules.MainDeckMin + "+ main"),
                12, legal ? DuelystUi.Cyan : DuelystUi.Danger, TextAnchor.MiddleCenter);
            FloatingPanel.Place(sub.rectTransform, 0.05f, 0.80f, 0.95f, 0.88f);

            var renameGo = new GameObject("Rename", typeof(RectTransform), typeof(Image), typeof(InputField));
            renameGo.transform.SetParent(phase, false);
            FloatingPanel.Place(renameGo.GetComponent<RectTransform>(), 0.10f, 0.68f, 0.90f, 0.78f);
            renameGo.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            renameGo.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 1f);
            var rText = Label(renameGo.transform, box?.name ?? "", 14, Color.white, TextAnchor.MiddleLeft);
            FloatingPanel.Stretch(rText.rectTransform, 8f);
            rText.raycastTarget = true;
            var rPh = Label(renameGo.transform, "Name...", 14, new Color(0.55f, 0.6f, 0.65f, 0.8f),
                TextAnchor.MiddleLeft);
            FloatingPanel.Stretch(rPh.rectTransform, 8f);
            var field = renameGo.GetComponent<InputField>();
            field.textComponent = rText;
            field.placeholder = rPh;
            field.text = box?.name ?? "";

            PlaceDeckRow(phase, 0.08f, 0.50f, 0.92f, 0.62f, "EDIT / VIEW CARDS", "open builder", true, () =>
            {
                st.Phase = Phase.Editor;
                st.Section = Section.Main;
                st.Rebuild();
            });
            PlaceDeckRow(phase, 0.08f, 0.38f, 0.92f, 0.48f, "RENAME", "save name above", false, () =>
            {
                var nm = field.text;
                if (string.IsNullOrWhiteSpace(nm))
                {
                    Status(st, "Enter a name.", false);
                    return;
                }

                st.Inv.RenameDeckBox(st.DeckIndex, nm);
                Save(st);
                st.Rebuild();
                Status(st, "Name saved", true);
            });
            PlaceDeckRow(phase, 0.08f, 0.26f, 0.92f, 0.36f, "CLEAR DECK", "remove all cards", false, () =>
            {
                st.Inv.ClearDeckBox(st.DeckIndex);
                Save(st);
                st.Rebuild();
                Status(st, "Deck emptied", true);
            });
            PlaceDeckRow(phase, 0.08f, 0.14f, 0.92f, 0.24f, "NEW DECK", "add empty box", false, () =>
            {
                if (!InventoryService.TryCreateDeckBox(st.Acc, out var last, out var err))
                {
                    Status(st, err ?? "Max deck boxes.", false);
                    return;
                }

                st.DeckIndex = last;
                Save(st);
                st.Phase = Phase.Editor;
                FreeUiKit.PlayConfirm();
                st.Rebuild();
            });
            PlaceDeckRow(phase, 0.08f, 0.02f, 0.92f, 0.12f, "< BACK TO BUILDER", "editor", false, () =>
            {
                st.Phase = Phase.Editor;
                st.Rebuild();
            });
        }

        static readonly (string Id, string Lab)[] DeckStrategyIcons =
        {
            ("deck", "Deck"), ("beatdown", "Beatdown"), ("burn", "Burn"),
            ("stall", "Defense"), ("aggro", "Aggro"), ("combo", "Combo"),
            ("mill", "Mill"), ("otk", "OTK"), ("control", "Control")
        };

        static readonly string[] DeckAttrIcons =
            { "attr_dark", "attr_light", "attr_earth", "attr_water", "attr_fire", "attr_wind", "attr_divine" };

        static readonly string[] DeckTypeIcons =
        {
            "type_dragon", "type_warrior", "type_spellcaster", "type_fiend", "type_machine",
            "type_zombie", "type_fairy", "type_beast", "type_wingedbeast", "type_cyberse",
            "type_psychic", "type_wyrm", "type_insect", "type_dinosaur",
            "type_rock", "type_plant", "type_aqua", "type_thunder", "type_pyro"
        };

        static string DeckIconId(State st, int index)
        {
            if (st?.Inv?.deckBoxes == null || index < 0 || index >= st.Inv.deckBoxes.Length)
                return "";
            return st.Inv.deckBoxes[index]?.iconId ?? "";
        }

        static void SetDeckIcon(State st, int index, string id)
        {
            if (st?.Inv == null) return;
            st.Inv.EnsureDeckBoxSlots();
            if (index < 0 || index >= st.Inv.deckBoxes.Length || st.Inv.deckBoxes[index] == null)
                return;
            st.Inv.deckBoxes[index].iconId = id ?? "";
            st.Inv.deckBoxes[index].occupied = true;
            Save(st);
        }

        static Text DeckNameLabel(Transform parent, string text, Color color)
        {
            var go = new GameObject("Name", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.StyleButtonLabel(t, 20, display: false);
            t.text = text ?? "Deck";
            t.color = color.a > 0.01f ? color : Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = false;
            t.raycastTarget = false;
            return t;
        }

        static Image DeckEmblem(Transform parent, string iconId)
        {
            var go = new GameObject("Emblem", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = ImagineAssets.DeckStyleIcon(iconId) ?? ImagineAssets.IconDeck()
                         ?? UiFoundation.WhiteSprite();
            img.preserveAspect = true;
            img.color = Color.white;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Icon + name + caret. The emblem is the visual (same path as M/Ex/S).
        /// Tap name to rename, tap icon to pick emblem, caret opens the list.
        /// </summary>
        static void BuildDeckSwitcher(RectTransform phase, State st, float x0, float y0, float x1, float y1)
        {
            st.Inv?.EnsureDeckBoxSlots();
            var idx = ClampDeck(st);
            var caption = DeckName(st);
            if (string.IsNullOrWhiteSpace(caption)) caption = "Deck";
            var iconId = DeckIconId(st, idx);

            var host = new GameObject("DeckSwitcher", typeof(RectTransform), typeof(Image), typeof(Canvas));
            host.transform.SetParent(phase, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var bg = host.GetComponent<Image>();
            // Simple, not 9-sliced: sliced HudChip borders eat the whole short row.
            bg.sprite = UiFoundation.WhiteSprite();
            bg.type = Image.Type.Simple;
            bg.color = new Color(0.12f, 0.18f, 0.28f, 1f);
            bg.raycastTarget = true;
            var edge = host.AddComponent<Outline>();
            edge.effectColor = new Color(0.40f, 0.85f, 1f, 0.85f);
            edge.effectDistance = new Vector2(2f, -2f);
            edge.useGraphicAlpha = false;
            var canvas = host.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 120;
            host.AddComponent<GraphicRaycaster>();

            var icoHit = new GameObject("IconHit", typeof(RectTransform), typeof(Image), typeof(Button));
            icoHit.transform.SetParent(host.transform, false);
            var icoRt = icoHit.GetComponent<RectTransform>();
            icoRt.anchorMin = new Vector2(0f, 0.5f);
            icoRt.anchorMax = new Vector2(0f, 0.5f);
            icoRt.pivot = new Vector2(0f, 0.5f);
            icoRt.anchoredPosition = new Vector2(6f, 0f);
            icoRt.sizeDelta = new Vector2(40f, 40f);
            var icoBg = icoHit.GetComponent<Image>();
            icoBg.sprite = UiFoundation.WhiteSprite();
            icoBg.color = new Color(1f, 1f, 1f, 0.06f);
            icoBg.raycastTarget = true;
            var emblem = DeckEmblem(icoHit.transform, iconId);
            FloatingPanel.Stretch(emblem.rectTransform, 1f);
            BindHold(icoHit, () => OpenIconPicker(phase, st, idx),
                () => OpenDeckContextMenu(phase, st, idx));

            var nameHit = new GameObject("NameHit", typeof(RectTransform), typeof(Image), typeof(Button));
            nameHit.transform.SetParent(host.transform, false);
            var nameRt = nameHit.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 0.12f);
            nameRt.anchorMax = new Vector2(1f, 0.88f);
            nameRt.offsetMin = new Vector2(48f, 0f);
            nameRt.offsetMax = new Vector2(-40f, 0f);
            var nBg = nameHit.GetComponent<Image>();
            nBg.sprite = UiFoundation.WhiteSprite();
            nBg.color = new Color(1f, 1f, 1f, 0f);
            nBg.raycastTarget = true;
            var nameT = DeckNameLabel(nameHit.transform, caption, Color.white);
            FloatingPanel.Stretch(nameT.rectTransform, 4f);
            BindHold(nameHit, () => OpenRenameSheet(phase, st, idx),
                () => OpenDeckContextMenu(phase, st, idx));

            var caretHit = new GameObject("Caret", typeof(RectTransform), typeof(Image), typeof(Button));
            caretHit.transform.SetParent(host.transform, false);
            var cRt = caretHit.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(1f, 0.5f);
            cRt.anchorMax = new Vector2(1f, 0.5f);
            cRt.pivot = new Vector2(1f, 0.5f);
            cRt.anchoredPosition = new Vector2(-4f, 0f);
            cRt.sizeDelta = new Vector2(36f, 36f);
            var cBg = caretHit.GetComponent<Image>();
            cBg.sprite = UiFoundation.WhiteSprite();
            cBg.color = new Color(1f, 1f, 1f, 0.08f);
            cBg.raycastTarget = true;
            var caretT = DeckNameLabel(caretHit.transform, "v", DuelystUi.Cyan);
            caretT.alignment = TextAnchor.MiddleCenter;
            caretT.resizeTextForBestFit = false;
            caretT.fontSize = 18;
            FloatingPanel.Stretch(caretT.rectTransform, 2f);
            BindHold(caretHit, () =>
            {
                CloseDeckMenu(st);
                CloseRenameSheet(st);
                if (st.DeckDropOpen) CloseDeckDropdown(st);
                else OpenDeckDropdownUp(phase, st, host.GetComponent<RectTransform>());
            }, () => OpenDeckContextMenu(phase, st, idx));
        }

        static void BindHold(GameObject go, Action tap, Action hold)
        {
            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                btn.transition = Selectable.Transition.None;
                btn.onClick.RemoveAllListeners();
                if (btn.targetGraphic == null)
                    btn.targetGraphic = go.GetComponent<Image>();
            }

            var h = go.GetComponent<DeckSwitcherHold>() ?? go.AddComponent<DeckSwitcherHold>();
            h.LongPressSec = LongPressSec;
            h.OnTap = () =>
            {
                FreeUiKit.PlayClick();
                tap?.Invoke();
            };
            h.OnLongPress = () =>
            {
                FreeUiKit.PlaySelect();
                hold?.Invoke();
            };
        }

        static void CloseDeckDropdown(State st)
        {
            st.DeckDropOpen = false;
            if (st.DeckDropHost != null)
            {
                UnityEngine.Object.Destroy(st.DeckDropHost);
                st.DeckDropHost = null;
            }
        }

        static void CloseDeckMenu(State st)
        {
            if (st?.DeckMenuHost != null)
            {
                UnityEngine.Object.Destroy(st.DeckMenuHost);
                st.DeckMenuHost = null;
            }
        }

        static void CloseRenameSheet(State st)
        {
            if (st?.DeckRenameHost != null)
            {
                UnityEngine.Object.Destroy(st.DeckRenameHost);
                st.DeckRenameHost = null;
            }
        }

        static void CommitRename(State st, int index, string raw, bool rebuild)
        {
            if (st?.Inv == null) return;
            var nm = (raw ?? "").Trim();
            var cur = st.Inv.DeckBoxDisplayName(index);
            if (string.IsNullOrWhiteSpace(nm))
            {
                if (st.NameField != null && index == ClampDeck(st))
                    st.NameField.text = cur ?? "Deck";
                Status(st, "Enter a name.", false);
                return;
            }

            if (string.Equals(nm, cur, StringComparison.Ordinal))
                return;

            st.Inv.RenameDeckBox(index, nm);
            Save(st);
            Status(st, "Renamed · " + nm, true);
            if (rebuild)
                st.Rebuild();
        }

        /// <summary>Hold-menu on a switcher deck: rename, new, more options.</summary>
        static void OpenDeckContextMenu(RectTransform phase, State st, int deckIndex)
        {
            CloseDeckDropdown(st);
            CloseDeckMenu(st);
            CloseRenameSheet(st);
            st.Inv?.EnsureDeckBoxSlots();
            var idx = Mathf.Clamp(deckIndex, 0, Mathf.Max(0, (st.Inv?.deckBoxes?.Length ?? 1) - 1));

            var host = new GameObject("DeckContext", typeof(RectTransform));
            host.transform.SetParent(phase, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            st.DeckMenuHost = host;

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(host.transform, false);
            FloatingPanel.Place(dim.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            var dimImg = dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = new Color(0f, 0f, 0f, 0.40f);
            dimImg.raycastTarget = true;
            dim.GetComponent<Button>().onClick.AddListener(() => CloseDeckMenu(st));

            const float rowH = 0.052f;
            const float gap = 0.007f;
            const int n = 4;
            var total = n * rowH + (n - 1) * gap;
            var y1 = ChromeNameY0 - 0.010f;
            var y0 = y1 - total;
            var sheet = new GameObject("Sheet", typeof(RectTransform), typeof(Image));
            sheet.transform.SetParent(host.transform, false);
            FloatingPanel.Place(sheet.GetComponent<RectTransform>(), 0.02f, y0, NamePillX1, y1);
            StyleGlass(sheet.GetComponent<Image>(), new Color(0.05f, 0.09f, 0.14f, 0.94f), EdgeCyan);
            sheet.GetComponent<Image>().raycastTarget = true;

            void Item(int i, string title, Action act, Color fill, Color edge)
            {
                var ry1 = 1f - i * ((rowH + gap) / Mathf.Max(0.001f, total));
                var ry0 = ry1 - rowH / Mathf.Max(0.001f, total);
                var b = GlassAction(sheet.transform, title, fill, edge, () =>
                {
                    CloseDeckMenu(st);
                    act();
                });
                FloatingPanel.Place(b.GetComponent<RectTransform>(), 0.04f, ry0 + 0.04f, 0.96f, ry1 - 0.04f);
            }

            Item(0, "RENAME", () => OpenRenameSheet(phase, st, idx),
                GlassCyan, EdgeCyan);
            Item(1, "CHOOSE ICON", () => OpenIconPicker(phase, st, idx), GlassCyan, EdgeCyan);
            Item(2, "NEW DECK", () => TryCreateDeckBox(st), GlassQuiet, EdgeCyan);
            Item(3, "MORE OPTIONS", () =>
            {
                PersistDeck(st);
                st.Phase = Phase.Actions;
                st.Rebuild();
            }, GlassQuiet, EdgeCyan);

            host.transform.SetAsLastSibling();
        }

        static void OpenRenameSheet(RectTransform phase, State st, int deckIndex)
        {
            CloseDeckDropdown(st);
            CloseDeckMenu(st);
            CloseRenameSheet(st);
            st.Inv?.EnsureDeckBoxSlots();
            var idx = Mathf.Clamp(deckIndex, 0, Mathf.Max(0, (st.Inv?.deckBoxes?.Length ?? 1) - 1));
            var cur = st.Inv != null ? st.Inv.DeckBoxDisplayName(idx) : "Deck";

            var host = new GameObject("RenameSheet", typeof(RectTransform));
            host.transform.SetParent(phase, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            st.DeckRenameHost = host;

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(host.transform, false);
            FloatingPanel.Place(dim.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            var dimImg = dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = new Color(0f, 0f, 0f, 0.50f);
            dimImg.raycastTarget = true;
            dim.GetComponent<Button>().onClick.AddListener(() => CloseRenameSheet(st));

            var sheet = new GameObject("Sheet", typeof(RectTransform), typeof(Image));
            sheet.transform.SetParent(host.transform, false);
            FloatingPanel.Place(sheet.GetComponent<RectTransform>(), 0.08f, 0.36f, 0.92f, 0.72f);
            StyleGlass(sheet.GetComponent<Image>(), new Color(0.05f, 0.09f, 0.14f, 0.96f), EdgeCyan);
            sheet.GetComponent<Image>().raycastTarget = true;

            var title = Label(sheet.transform, "RENAME DECK", 16, DuelystUi.Cyan, TextAnchor.MiddleCenter);
            StyleChromeType(title, 16, TextAnchor.MiddleCenter, DuelystUi.Cyan);
            FloatingPanel.Place(title.rectTransform, 0.20f, 0.78f, 0.94f, 0.96f);

            var icoHit = new GameObject("IconHit", typeof(RectTransform), typeof(Image), typeof(Button));
            icoHit.transform.SetParent(sheet.transform, false);
            FloatingPanel.Place(icoHit.GetComponent<RectTransform>(), 0.04f, 0.76f, 0.18f, 0.96f);
            var icoBg = icoHit.GetComponent<Image>();
            icoBg.sprite = UiFoundation.WhiteSprite();
            icoBg.color = new Color(1f, 1f, 1f, 0.06f);
            var preview = DeckEmblem(icoHit.transform, DeckIconId(st, idx));
            FloatingPanel.Stretch(preview.rectTransform, 2f);
            icoHit.GetComponent<Button>().onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                CloseRenameSheet(st);
                OpenIconPicker(phase, st, idx);
            });

            var fieldGo = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(InputField));
            fieldGo.transform.SetParent(sheet.transform, false);
            FloatingPanel.Place(fieldGo.GetComponent<RectTransform>(), 0.06f, 0.40f, 0.94f, 0.72f);
            StyleGlass(fieldGo.GetComponent<Image>(), new Color(0.04f, 0.07f, 0.12f, 0.90f), EdgeCyan);
            var fText = Label(fieldGo.transform, cur ?? "", 16, DuelystUi.TextCream, TextAnchor.MiddleLeft);
            StyleChromeType(fText, 16, TextAnchor.MiddleLeft, DuelystUi.TextCream);
            fText.raycastTarget = true;
            FloatingPanel.Place(fText.rectTransform, 0.04f, 0.10f, 0.96f, 0.90f);
            var fPh = Label(fieldGo.transform, "Deck name", 14,
                new Color(0.58f, 0.72f, 0.82f, 0.70f), TextAnchor.MiddleLeft);
            fPh.raycastTarget = false;
            FloatingPanel.Place(fPh.rectTransform, 0.04f, 0.10f, 0.96f, 0.90f);
            var field = fieldGo.GetComponent<InputField>();
            field.textComponent = fText;
            field.placeholder = fPh;
            field.text = cur ?? "";
            field.characterLimit = 40;
            field.lineType = InputField.LineType.SingleLine;
            field.caretColor = DuelystUi.Cyan;

            var save = GlassAction(sheet.transform, "SAVE", GlassCyan, EdgeCyan, () =>
            {
                CommitRename(st, idx, field.text, rebuild: true);
                CloseRenameSheet(st);
            });
            FloatingPanel.Place(save.GetComponent<RectTransform>(), 0.52f, 0.08f, 0.94f, 0.32f);

            var cancel = GlassAction(sheet.transform, "CANCEL", GlassQuiet, EdgeCyan, () => CloseRenameSheet(st));
            FloatingPanel.Place(cancel.GetComponent<RectTransform>(), 0.06f, 0.08f, 0.46f, 0.32f);

            host.transform.SetAsLastSibling();
            field.ActivateInputField();
            field.caretPosition = (field.text ?? "").Length;
        }

        static void TryCreateDeckBox(State st)
        {
            if (st?.Inv == null) return;
            PersistDeck(st);
            if (!InventoryService.TryCreateDeckBox(st.Acc, out var last, out var err))
            {
                Status(st, err ?? "Max deck boxes.", false);
                return;
            }

            st.DeckIndex = last;
            Save(st);
            st.Phase = Phase.Editor;
            FreeUiKit.PlayConfirm();
            st.Rebuild();
            Status(st, "New deck ready — tap the name to rename.", true);
        }

        /// <summary>
        /// Pick a Yugipedia-style strategy / attribute / type emblem for this deck box.
        /// </summary>
        static void OpenIconPicker(RectTransform phase, State st, int deckIndex)
        {
            CloseDeckDropdown(st);
            CloseDeckMenu(st);
            CloseRenameSheet(st);
            var idx = Mathf.Clamp(deckIndex, 0, Mathf.Max(0, (st.Inv?.deckBoxes?.Length ?? 1) - 1));
            var cur = DeckIconId(st, idx);

            var host = new GameObject("IconPicker", typeof(RectTransform));
            host.transform.SetParent(phase, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            st.DeckRenameHost = host;

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(host.transform, false);
            FloatingPanel.Place(dim.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            var dimImg = dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = new Color(0f, 0f, 0f, 0.55f);
            dimImg.raycastTarget = true;
            dim.GetComponent<Button>().onClick.AddListener(() => CloseRenameSheet(st));

            var sheet = new GameObject("Sheet", typeof(RectTransform), typeof(Image));
            sheet.transform.SetParent(host.transform, false);
            FloatingPanel.Place(sheet.GetComponent<RectTransform>(), 0.04f, 0.10f, 0.96f, 0.88f);
            StyleGlass(sheet.GetComponent<Image>(), new Color(0.05f, 0.09f, 0.14f, 0.96f), EdgeCyan);
            sheet.GetComponent<Image>().raycastTarget = true;

            var title = DeckNameLabel(sheet.transform, "CHOOSE DECK ICON", DuelystUi.Cyan);
            title.alignment = TextAnchor.MiddleCenter;
            title.resizeTextMaxSize = 18;
            FloatingPanel.Place(title.rectTransform, 0.04f, 0.90f, 0.70f, 0.98f);

            var done = GlassAction(sheet.transform, "DONE", GlassCyan, EdgeCyan, () =>
            {
                CloseRenameSheet(st);
                st.Rebuild();
            });
            FloatingPanel.Place(done.GetComponent<RectTransform>(), 0.72f, 0.90f, 0.96f, 0.98f);

            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(sheet.transform, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.03f, 0.04f, 0.97f, 0.88f);
            var sBg = scroll.GetComponent<Image>();
            sBg.sprite = UiFoundation.WhiteSprite();
            sBg.color = new Color(1f, 1f, 1f, 0.02f);

            var vp = new GameObject("VP", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            vp.transform.SetParent(scroll.transform, false);
            FloatingPanel.Stretch(vp.GetComponent<RectTransform>(), 2f);
            vp.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            vp.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);

            var content = new GameObject("C", typeof(RectTransform), typeof(GridLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(vp.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var g = content.GetComponent<GridLayoutGroup>();
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 6;
            g.cellSize = new Vector2(72f, 78f);
            g.spacing = new Vector2(8f, 8f);
            g.padding = new RectOffset(6, 6, 6, 6);
            g.childAlignment = TextAnchor.UpperLeft;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sc = scroll.GetComponent<ScrollRect>();
            sc.viewport = vp.GetComponent<RectTransform>();
            sc.content = crt;
            sc.horizontal = false;
            sc.vertical = true;
            sc.movementType = ScrollRect.MovementType.Clamped;

            void Tile(string id, string lab)
            {
                var on = string.Equals(cur, id, StringComparison.OrdinalIgnoreCase)
                         || (string.IsNullOrEmpty(cur) && id == "deck");
                var cell = new GameObject("I_" + id, typeof(RectTransform), typeof(Image), typeof(Button));
                cell.transform.SetParent(content.transform, false);
                var cImg = cell.GetComponent<Image>();
                cImg.sprite = UiFoundation.WhiteSprite();
                cImg.color = on
                    ? new Color(0.18f, 0.55f, 0.68f, 0.70f)
                    : new Color(0.08f, 0.10f, 0.14f, 0.45f);
                var emblem = DeckEmblem(cell.transform, id);
                FloatingPanel.Place(emblem.rectTransform, 0.10f, 0.28f, 0.90f, 0.96f);
                var cap = DeckNameLabel(cell.transform, lab, on ? DuelystUi.Cyan : DuelystUi.TextCream);
                cap.alignment = TextAnchor.MiddleCenter;
                cap.fontSize = 11;
                cap.resizeTextMinSize = 8;
                cap.resizeTextMaxSize = 12;
                FloatingPanel.Place(cap.rectTransform, 0.02f, 0.00f, 0.98f, 0.28f);
                var captured = id;
                cell.GetComponent<Button>().onClick.AddListener(() =>
                {
                    FreeUiKit.PlaySelect();
                    SetDeckIcon(st, idx, captured == "deck" ? "" : captured);
                    CloseRenameSheet(st);
                    st.Rebuild();
                });
            }

            foreach (var s in DeckStrategyIcons)
                Tile(s.Id, s.Lab);
            foreach (var a in DeckAttrIcons)
                Tile(a, a.Replace("attr_", "").ToUpperInvariant());
            foreach (var t in DeckTypeIcons)
                Tile(t, t.Replace("type_", ""));

            host.transform.SetAsLastSibling();
        }

        /// <summary>On-hand decks rise as tight boxes from the switcher.</summary>
        static void OpenDeckDropdownUp(RectTransform phase, State st, RectTransform anchorBtn)
        {
            CloseDeckMenu(st);
            CloseRenameSheet(st);
            CloseDeckDropdown(st);
            st.Inv?.EnsureDeckBoxSlots();
            var hands = st.Inv != null ? st.Inv.OnHandDeckIndices() : new List<int>();
            if (st.DeckIndex >= 0 && !hands.Contains(st.DeckIndex))
                hands.Insert(0, st.DeckIndex);

            var n = Mathf.Max(1, hands.Count);
            const float rowH = 0.050f;
            const float gap = 0.007f;
            var totalH = n * rowH + Mathf.Max(0, n - 1) * gap;
            const float y1 = ChromeNameY0 - 0.006f;
            var y0 = Mathf.Max(0.12f, y1 - totalH);

            var host = new GameObject("DeckDropdown", typeof(RectTransform), typeof(Image),
                typeof(Canvas), typeof(GraphicRaycaster));
            host.transform.SetParent(phase, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), 0f, y0, NamePillX1, y1);
            var hostImg = host.GetComponent<Image>();
            hostImg.sprite = UiFoundation.WhiteSprite();
            hostImg.type = Image.Type.Simple;
            hostImg.color = new Color(0.04f, 0.07f, 0.12f, 0.04f);
            hostImg.raycastTarget = false;
            var dropCanvas = host.GetComponent<Canvas>();
            dropCanvas.overrideSorting = true;
            dropCanvas.sortingOrder = 130;
            st.DeckDropHost = host;
            st.DeckDropOpen = true;

            var runner = host.AddComponent<DeckDropAnim>();
            if (hands.Count == 0)
            {
                var empty = new GameObject("Empty", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                empty.transform.SetParent(host.transform, false);
                FloatingPanel.Place(empty.GetComponent<RectTransform>(), 0.02f, 0.08f, 0.98f, 0.92f);
                StyleGlass(empty.GetComponent<Image>(), GlassQuiet, EdgeCyan);
                empty.GetComponent<Image>().raycastTarget = false;
                var emptyT = DeckNameLabel(empty.transform, "No decks on hand", DuelystUi.TextMuted);
                emptyT.alignment = TextAnchor.MiddleCenter;
                FloatingPanel.Place(emptyT.rectTransform, 0.06f, 0.12f, 0.94f, 0.88f);
                var cg = empty.GetComponent<CanvasGroup>();
                cg.alpha = 0f;
                runner.StartCoroutine(RiseBox(empty.GetComponent<RectTransform>(), cg, 0f));
            }
            else
            for (var i = 0; i < n; i++)
            {
                var idx = hands[i];
                var box = st.Inv.deckBoxes != null && idx >= 0 && idx < st.Inv.deckBoxes.Length
                    ? st.Inv.deckBoxes[idx] : null;
                var nm = st.Inv.DeckBoxDisplayName(idx);
                var mainN = box?.main?.Length ?? 0;
                var selected = idx == st.DeckIndex;
                var ry1 = 1f - i * ((rowH + gap) / Mathf.Max(0.001f, totalH));
                var ry0 = ry1 - rowH / Mathf.Max(0.001f, totalH);

                var row = new GameObject("DeckBox_" + idx, typeof(RectTransform), typeof(Image),
                    typeof(Button), typeof(CanvasGroup));
                row.transform.SetParent(host.transform, false);
                FloatingPanel.Place(row.GetComponent<RectTransform>(), 0.02f, ry0, 0.98f, ry1);
                var img = row.GetComponent<Image>();
                img.raycastTarget = true;
                StyleGlass(img, selected ? GlassOn : GlassQuiet,
                    selected ? EdgeCyan : new Color(0.35f, 0.80f, 0.95f, 0.22f));

                var emblem = DeckEmblem(row.transform, box?.iconId ?? "");
                FloatingPanel.Place(emblem.rectTransform, 0.02f, 0.08f, 0.18f, 0.92f);

                var titleT = DeckNameLabel(row.transform, nm ?? "Deck",
                    selected ? DuelystUi.Cyan : Color.white);
                FloatingPanel.Place(titleT.rectTransform, 0.20f, 0.08f, 0.62f, 0.92f);

                var metaT = DeckNameLabel(row.transform,
                    mainN + (selected ? " MAIN" : " main"),
                    selected ? DuelystUi.Cyan : DuelystUi.TextMuted);
                metaT.alignment = TextAnchor.MiddleRight;
                FloatingPanel.Place(metaT.rectTransform, 0.62f, 0.12f, 0.96f, 0.88f);

                var btn = row.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.transition = Selectable.Transition.None;
                btn.onClick.RemoveAllListeners();
                var captured = idx;
                var capturedNm = nm;
                var hold = row.AddComponent<DeckSwitcherHold>();
                hold.LongPressSec = LongPressSec;
                hold.OnTap = () =>
                {
                    FreeUiKit.PlayClick();
                    if (captured == st.DeckIndex)
                    {
                        CloseDeckDropdown(st);
                        return;
                    }

                    TrySwitchDeck(st, captured, capturedNm);
                };
                hold.OnLongPress = () =>
                {
                    FreeUiKit.PlaySelect();
                    OpenDeckContextMenu(phase, st, captured);
                };

                var cg = row.GetComponent<CanvasGroup>();
                cg.alpha = 0f;
                runner.StartCoroutine(RiseBox(row.GetComponent<RectTransform>(), cg, i * 0.045f));
            }

            host.transform.SetAsLastSibling();
        }

        static IEnumerator RiseBox(RectTransform rt, CanvasGroup cg, float delay)
        {
            if (rt == null) yield break;
            var rest = rt.anchoredPosition;
            rt.anchoredPosition = rest + new Vector2(0f, -46f);
            if (cg != null) cg.alpha = 0f;
            var wait = 0f;
            while (wait < delay)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            const float dur = 0.22f;
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var k = MenuMotion.OutCubic(t / dur);
                rt.anchoredPosition = Vector2.Lerp(rest + new Vector2(0f, -46f), rest, k);
                if (cg != null) cg.alpha = k;
                yield return null;
            }

            rt.anchoredPosition = rest;
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
            }
        }

        sealed class DeckDropAnim : MonoBehaviour { }

        /// <summary>Apply search after a short pause so typing doesn't rebuild every key.</summary>
        sealed class SearchLive : MonoBehaviour
        {
            InputField _field;
            Action<string> _apply;
            float _due = -1f;
            string _pending;

            public void Bind(InputField field, Action<string> apply)
            {
                _field = field;
                _apply = apply;
                if (_field != null)
                    _field.onValueChanged.AddListener(OnChanged);
            }

            void OnChanged(string v)
            {
                _pending = v ?? "";
                _due = Time.unscaledTime + 0.18f;
            }

            void Update()
            {
                if (_due < 0f || Time.unscaledTime < _due) return;
                _due = -1f;
                _apply?.Invoke(_pending);
            }
        }

        /// <summary>Tap vs long-press for the deck name control.</summary>
        sealed class DeckSwitcherHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            public float LongPressSec = 0.45f;
            public Action OnTap;
            public Action OnLongPress;
            float _down;
            bool _held;
            bool _longFired;

            public void OnPointerDown(PointerEventData eventData)
            {
                _down = Time.unscaledTime;
                _held = true;
                _longFired = false;
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                if (!_held)
                {
                    _held = false;
                    return;
                }

                _held = false;
                if (_longFired) return;
                // Short tap
                OnTap?.Invoke();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                // Cancel tap if drag-off, but keep long-press if already firing
                if (!_longFired)
                    _held = false;
            }

            void Update()
            {
                if (!_held || _longFired) return;
                if (Time.unscaledTime - _down < LongPressSec) return;
                _longFired = true;
                _held = false;
                OnLongPress?.Invoke();
            }
        }

        // ── Compact editor ───────────────────────────────────────────────────

        static bool HasChipRow(State st) =>
            HasAdvancedFilters(st) || st.Filter != Filter.All || !string.IsNullOrWhiteSpace(st.Search);

        static void BuildEditor(RectTransform phase, State st)
        {
            var ar = IsAr(st);
            var wide = !ar && IsWideLayout(phase);
            const float barH = 72f;
            const float barTop = 8f;
            const float row2 = 88f;
            const float row2H = 64f;
            const float searchH = 52f;
            var searchTop = wide ? 88f : 160f;
            var chromeBottom = searchTop + searchH + 8f;
            BuildDeckSwitcher(phase, st, 0f, ChromeNameY0, ar ? NamePillX1 : SwitcherX1, ChromeNameY1);
            var switcher = phase.Find("DeckSwitcher") as RectTransform;

            var close = GlassAction(phase, "X", GlassQuiet, EdgeCyan, () =>
            {
                FreeUiKit.PlayClick();
                RequestLeaveEditor(st);
            });

            if (ar)
            {
                if (switcher != null) PinTop(switcher, 0f, 0.86f, barTop, barH);
                PinTop(close.GetComponent<RectTransform>(), 0.88f, 1f, barTop, barH);
            }
            else if (wide)
            {
                if (switcher != null) PinTop(switcher, 0f, 0.48f, barTop, barH);
                PinTop(close.GetComponent<RectTransform>(), 0.88f, 1f, barTop, barH);
            }
            else
            {
                if (switcher != null) PinTop(switcher, 0f, 0.82f, barTop, barH);
                PinTop(close.GetComponent<RectTransform>(), 0.84f, 1f, barTop, barH);
            }

            if (!ar)
            {
                var save = GlassAction(phase, "SAVE", GlassGold, EdgeGold, () =>
                {
                    if (!DeckIsComplete(st))
                    {
                        FreeUiKit.PlayClick();
                        Status(st, IncompleteDeckLabel(st), false);
                        return;
                    }

                    PersistDeck(st);
                    FreeUiKit.PlayConfirm();
                    Status(st, "Saved · ready for VS AI", true);
                });
                var clear = GlassAction(phase, "CLEAR", GlassRose, EdgeRose, () =>
                {
                    st.Main.Clear();
                    st.Extra.Clear();
                    st.Side.Clear();
                    st.Inv.ClearDeckBox(st.DeckIndex);
                    PersistDeck(st);
                    FreeUiKit.PlayClick();
                    st.Rebuild();
                    Status(st, "Deck cleared", true);
                });
                if (wide)
                {
                    PinTop(save.GetComponent<RectTransform>(), 0.50f, 0.68f, barTop, barH);
                    PinTop(clear.GetComponent<RectTransform>(), 0.70f, 0.86f, barTop, barH);
                }
                else
                {
                    PinTop(save.GetComponent<RectTransform>(), 0.00f, 0.48f, row2, row2H);
                    PinTop(clear.GetComponent<RectTransform>(), 0.52f, 1.00f, row2, row2H);
                }

                BuildFilterBar(phase, st, wide, searchTop, searchH);
                if (wide)
                {
                    BuildConstructionBoard(phase, st, 0.00f, 0.00f, 0.615f, 0.840f);
                    PinBelow(phase.Find("ConstructionBoard") as RectTransform,
                        0f, 0.615f, chromeBottom, 0f);
                    var listContent = ChipGrid(phase, st, 0.630f, 0.00f, 1f, 0.800f, "PoolGrid",
                        out st.PoolScroll);
                    st.PoolGrid = listContent;
                    // ChipGrid returns the scroll *content*. Pinning that with
                    // phase-normalized x0=0.630 packed chips into the right 37%
                    // of the already-right CARD LIST well (empty holo, clipped
                    // column). Pin the panel instead.
                    PinBelow(st.PoolScroll.GetComponent<RectTransform>(),
                        0.630f, 1f, chromeBottom + 28f, 0f);
                    RefitPool(st);
                    var listHead = Label(phase, "CARD LIST", 12, DuelystUi.GoldHot, TextAnchor.MiddleLeft);
                    PinTop(listHead.rectTransform, 0.630f, 0.82f, chromeBottom, 26f);
                    st.MatchLine = L(phase, "", 11, DuelystUi.TextMuted, TextAnchor.MiddleRight);
                    PinTop(st.MatchLine.rectTransform, 0.82f, 1f, chromeBottom, 26f);
                }
                else
                {
                    BuildConstructionBoard(phase, st, 0.00f, 0.360f, 1f, 0.840f);
                    PinBelow(phase.Find("ConstructionBoard") as RectTransform,
                        0f, 1f, chromeBottom, 0.360f);
                    var listPanel = ChipGrid(phase, st, 0f, 0.00f, 1f, 0.320f, "PoolGrid",
                        out st.PoolScroll);
                    st.PoolGrid = listPanel;
                    RefitPool(st);
                    var listHead = Label(phase, "CARD LIST", 12, DuelystUi.GoldHot, TextAnchor.MiddleLeft);
                    FloatingPanel.Place(listHead.rectTransform, 0.02f, 0.320f, 0.40f, 0.358f);
                    st.MatchLine = L(phase, "", 11, DuelystUi.TextMuted, TextAnchor.MiddleRight);
                    FloatingPanel.Place(st.MatchLine.rectTransform, 0.40f, 0.320f, 0.98f, 0.358f);
                }
            }
            else
            {
                var hint = Label(phase, "full builder on PHONE", 11, DuelystUi.TextMuted,
                    TextAnchor.MiddleLeft);
                FloatingPanel.Place(hint.rectTransform, 0.02f, 0.848f, 0.70f, 0.900f);
                BuildConstructionBoard(phase, st, 0.00f, 0.00f, 1f, 0.840f);
            }

            st.RefreshCards = () => FillCardGrids(st, resetScroll: false);
            FillCardGrids(st);
        }

        /// <summary>
        /// Tap COLLECTION or DECK to aim search/filters at that grid.
        /// Tap the already-active side again to apply to both.
        /// </summary>
        static void BuildScopeHeader(RectTransform phase, State st,
            float x0, float y0, float x1, float y1, FilterScope target, string label)
        {
            var exclusive = st.Scope == target;
            var applying = FilterAppliesTo(st, target);
            var text = exclusive ? label + " · FILTER" : label;
            var fill = exclusive
                ? new Color(0.55f, 0.90f, 1f, 0.22f)
                : applying
                    ? new Color(0.10f, 0.38f, 0.48f, 0.45f)
                    : new Color(0.09f, 0.11f, 0.16f, 0.35f);
            var b = DeckChromeButton(phase, text, () =>
            {
                st.Scope = exclusive ? FilterScope.Both : target;
                FreeUiKit.PlaySelect();
                st.Rebuild();
            }, fill, compact: true);
            FloatingPanel.Place(b.GetComponent<RectTransform>(), x0, y0, x1, y1);
        }

        static bool FilterAppliesToPool(State st) => true;
        static bool FilterAppliesToDeck(State st) => false;
        static bool FilterAppliesTo(State st, FilterScope target) =>
            target == FilterScope.Collection ? FilterAppliesToPool(st) : FilterAppliesToDeck(st);

        static void MarkFilterTarget(Transform gridContent, bool on)
        {
            var panel = gridContent != null && gridContent.parent != null
                ? gridContent.parent.parent
                : null;
            if (panel == null) return;
            var ol = panel.GetComponent<Outline>() ?? panel.gameObject.AddComponent<Outline>();
            ol.enabled = on;
            ol.effectColor = new Color(0.55f, 0.90f, 1f, on ? 0.45f : 0f);
            ol.effectDistance = new Vector2(2.2f, -2.2f);
            ol.useGraphicAlpha = false;
            var img = panel.GetComponent<Image>();
            if (img != null)
                img.color = on
                    ? new Color(0.04f, 0.06f, 0.10f, 0.22f)
                    : new Color(0.02f, 0.03f, 0.06f, 0.08f);
        }

        static void FillCardGrids(State st, bool resetScroll = true)
        {
            if (st.BoardHost == null && st.PoolGrid == null) return;
            var keepPool = resetScroll ? 0 : st.PoolVisible;
            var poolY = 0f;
            if (!resetScroll && st.PoolScroll != null && st.PoolScroll.content != null)
                poolY = st.PoolScroll.content.anchoredPosition.y;

            if (st.PoolGrid != null)
            {
                FloatingPanel.DestroyChildrenNow(st.PoolGrid);
                ClearEmptyHint(st.PoolGrid);

                st.PoolRows = BuildPool(st);
                st.PoolVisible = 0;
                AppendPoolPage(st);
                if (!resetScroll)
                {
                    var guard = 0;
                    while (st.PoolRows != null && st.PoolVisible < keepPool
                           && st.PoolVisible < st.PoolRows.Count && guard++ < 64)
                        AppendPoolPage(st);
                }

                EnsurePoolFillsViewport(st);
                if (st.PoolRows.Count == 0)
                {
                    var idlePool = string.IsNullOrWhiteSpace(st.Search)
                                   && st.Filter == Filter.All && !HasAdvancedFilters(st);
                    ShowEmptyHint(st.PoolGrid,
                        idlePool
                            ? "No cards in this collection."
                            : "No matches.\nClear filters or try another search.");
                }

                WriteMatchLine(st, -1);
                RefitPool(st);
                if (resetScroll)
                {
                    if (st.PoolScroll != null) st.PoolScroll.verticalNormalizedPosition = 1f;
                }
                else RestoreScrollY(st.PoolScroll, poolY);
            }

            FillConstructionBoard(st);
        }

        static void RestoreScrollY(ScrollRect scroll, float contentY)
        {
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            scroll.content.GetComponent<DeckPoolFit>()?.Fit(force: true);
            var max = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
            var y = Mathf.Clamp(contentY, 0f, max);
            var pos = scroll.content.anchoredPosition;
            pos.y = y;
            scroll.content.anchoredPosition = pos;
        }

        static void WriteMatchLine(State st, int deckShown = -1)
        {
            if (st?.MatchLine == null) return;
            var total = st.PoolRows != null ? st.PoolRows.Count : 0;
            var shown = st.PoolVisible;
            var more = total > shown ? " · scroll" : "";
            if (deckShown < 0)
            {
                st.MatchLine.text = shown + " / " + total + " collection" + more + " · " + SortLabel(st.Sort);
                st.MatchLine.color = DuelystUi.TextMuted;
                return;
            }

            st.MatchLine.text = st.Scope switch
            {
                FilterScope.Collection => shown + " / " + total + " collection" + more,
                FilterScope.Deck => deckShown + " shown · deck",
                _ => shown + " / " + total + " pool" + more + " · " + deckShown + " deck"
            } + " · " + SortLabel(st.Sort);
            st.MatchLine.color = DuelystUi.TextMuted;
        }

        static void AppendPoolPage(State st)
        {
            if (st?.PoolGrid == null || st.PoolRows == null) return;
            if (st.PoolVisible >= st.PoolRows.Count) return;
            st.PoolAppending = true;
            var end = Mathf.Min(st.PoolVisible + PoolPageSize, st.PoolRows.Count);
            for (var i = st.PoolVisible; i < end; i++)
                PoolChip(st, st.PoolGrid, st.PoolRows[i]);
            st.PoolVisible = end;
            st.PoolAppending = false;
            RefitPool(st);
        }

        static void EnsurePoolFillsViewport(State st)
        {
            if (st?.PoolScroll == null || st.PoolScroll.viewport == null || st.PoolScroll.content == null)
                return;
            var guard = 0;
            while (st.PoolRows != null && st.PoolVisible < st.PoolRows.Count && guard++ < 24)
            {
                RefitPool(st);
                var viewH = st.PoolScroll.viewport.rect.height;
                var contentH = st.PoolScroll.content.rect.height;
                if (viewH < 8f || contentH > viewH + 24f) break;
                var before = st.PoolVisible;
                AppendPoolPage(st);
                if (st.PoolVisible == before) break;
            }
        }

        static void TryAppendPool(State st)
        {
            if (st == null || st.PoolAppending) return;
            if (st.PoolRows == null || st.PoolVisible >= st.PoolRows.Count) return;
            var scroll = st.PoolScroll;
            if (scroll == null || scroll.viewport == null || scroll.content == null) return;
            var viewH = scroll.viewport.rect.height;
            var contentH = scroll.content.rect.height;
            if (viewH < 8f) return;
            // 1 = top, 0 = bottom. Keep filling if the first page is shorter than the pane.
            var needsFill = contentH <= viewH + 12f;
            var nearBottom = scroll.verticalNormalizedPosition <= 0.16f;
            if (!needsFill && !nearBottom) return;
            AppendPoolPage(st);
            WriteMatchLine(st);
        }

        static void ClearEmptyHint(Transform gridContent)
        {
            var panel = gridContent != null && gridContent.parent != null ? gridContent.parent.parent : null;
            if (panel == null) return;
            var old = panel.Find("EmptyHint");
            if (old != null) FloatingPanel.DestroyNow(old.gameObject);
        }

        static void ShowEmptyHint(Transform gridContent, string msg)
        {
            ClearEmptyHint(gridContent);
            var panel = gridContent != null && gridContent.parent != null ? gridContent.parent.parent : gridContent;
            if (panel == null) return;
            var empty = L(panel, msg, 13, DuelystUi.TextMuted, TextAnchor.MiddleCenter);
            empty.gameObject.name = "EmptyHint";
            FloatingPanel.Place(empty.rectTransform, 0.08f, 0.32f, 0.92f, 0.68f);
        }

        static IComparable DeckSortKey(State st, int id)
        {
            var def = st.Db?.Get(id);
            return st.Sort switch
            {
                SortMode.Level => (IComparable)(def?.level ?? 0),
                SortMode.Atk => def?.atk ?? -1,
                SortMode.Def => def?.def ?? -1,
                SortMode.Type => def?.type ?? "",
                _ => def?.name ?? ""
            };
        }

        class PoolRow
        {
            public int Id;
            public int OnHand;
            public int AtHome;
            public bool HomeOnly;
            public CardDef Def;
        }

        static List<PoolRow> BuildPool(State st)
        {
            var ids = new HashSet<int>();
            if (st.Inv?.storageBoxes != null)
            {
                foreach (var box in st.Inv.storageBoxes)
                {
                    if (box?.stacks == null) continue;
                    foreach (var s in box.stacks)
                        if (s != null && s.cardId > 0) ids.Add(s.cardId);
                }
            }

            var q = (st.Search ?? "").Trim();
            var filterPool = FilterAppliesToPool(st);
            var deckIds = st.InDeck != InDeckFilter.Any ? DeckIdSet(st) : null;
            var rows = new List<PoolRow>();
            foreach (var id in ids)
            {
                var def = st.Db?.Get(id);
                if (filterPool)
                {
                    if (!PassFilter(st, def)) continue;
                    if (!PassSearch(q, id, def)) continue;
                    if (!PassArchetype(st, def)) continue;
                    if (!PassInDeck(st, id, deckIds)) continue;
                }

                var on = st.Inv.CountOnHand(id);
                var home = st.Inv.CountAtHome(id);
                rows.Add(new PoolRow
                {
                    Id = id,
                    OnHand = on,
                    AtHome = home,
                    HomeOnly = on <= 0 && home > 0,
                    Def = def
                });
            }

            return rows
                .OrderBy(r => r.HomeOnly ? 1 : 0)
                .ThenBy(r => SearchRank(q, r.Id, r.Def))
                .ThenBy(r => DeckSortKey(st, r.Id))
                .ThenBy(r => r.Def?.name ?? "")
                .ToList();
        }

        /// <summary>
        /// Tokenized search. Spaces are AND. Supports name / id / archetype / text /
        /// type / race / attribute plus shortcuts: lv7, lv9+, atk&gt;2000, def&lt;1500,
        /// dark, dragon, spell, trap, extra.
        /// </summary>
        static bool PassSearch(string q, int id, CardDef def)
        {
            if (string.IsNullOrWhiteSpace(q)) return true;
            var tokens = q.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
                if (!TokenMatches(t.Trim(), id, def))
                    return false;
            return true;
        }

        static bool TokenMatches(string token, int id, CardDef def)
        {
            if (string.IsNullOrEmpty(token)) return true;
            if (id.ToString() == token) return true;
            if (def == null) return false;

            var low = token.ToLowerInvariant();

            if (TryLevelToken(low, def, out var lvOk)) return lvOk;
            if (TryStatToken(low, "atk", def.atk, def.IsMonster, out var atkOk)) return atkOk;
            if (TryStatToken(low, "def", def.def, def.IsMonster, out var defOk)) return defOk;

            if (low is "spell" or "spells" or "spl") return def.IsSpell;
            if (low is "trap" or "traps" or "trp") return def.IsTrap;
            if (low is "monster" or "mon" or "monsters") return def.IsMonster;
            if (low is "extra" or "ex") return def.IsExtraDeck;
            if (low is "fusion" or "synchro" or "xyz" or "link" or "ritual"
                or "pendulum" or "pend" or "tuner" or "flip" or "spirit" or "union" or "toon")
                return Contains(def.type, low) || Contains(def.frameType, low);
            if (low is "continuous" or "cont") return KindEquals(def.race, "Continuous");
            if (low is "equip" or "eq") return KindEquals(def.race, "Equip");
            if (low is "field" or "fld") return KindEquals(def.race, "Field");
            if (low is "quick-play" or "quickplay" or "qp") return KindEquals(def.race, "Quick-Play");
            if (low is "counter" or "ctr") return def.IsTrap && KindEquals(def.race, "Counter");

            for (var i = 0; i < AttributeOptions.Length; i++)
            {
                if (!AttributeOptions[i].Equals(token, StringComparison.OrdinalIgnoreCase) &&
                    !AttributeAbbr[i].Equals(token, StringComparison.OrdinalIgnoreCase))
                    continue;
                return def.IsMonster && Contains(def.attribute, AttributeOptions[i]);
            }

            for (var i = 0; i < MonsterTypeOptions.Length; i++)
            {
                if (!MonsterTypeOptions[i].Equals(token, StringComparison.OrdinalIgnoreCase) &&
                    !MonsterTypeAbbr[i].Equals(token, StringComparison.OrdinalIgnoreCase))
                    continue;
                return def.IsMonster && Contains(def.race, MonsterTypeOptions[i]);
            }

            return Contains(def.name, token) ||
                   Contains(def.archetype, token) ||
                   Contains(def.type, token) ||
                   Contains(def.race, token) ||
                   Contains(def.attribute, token) ||
                   Contains(def.frameType, token) ||
                   Contains(def.desc, token);
        }

        static bool TryLevelToken(string low, CardDef def, out bool ok)
        {
            ok = false;
            string rest = null;
            if (low.StartsWith("level")) rest = low.Substring(5);
            else if (low.StartsWith("lv")) rest = low.Substring(2);
            if (rest == null) return false;
            rest = rest.TrimStart();
            var plus = rest.EndsWith("+");
            if (plus) rest = rest.TrimEnd('+');
            if (!int.TryParse(rest, out var lv)) return false;
            if (def == null || !def.IsMonster) { ok = false; return true; }
            ok = plus ? def.level >= lv : def.level == lv;
            return true;
        }

        static bool TryStatToken(string low, string key, int value, bool isMonster, out bool ok)
        {
            ok = false;
            if (!low.StartsWith(key, StringComparison.Ordinal)) return false;
            var rest = low.Substring(key.Length);
            if (rest.Length == 0) return false;
            char op = '=';
            var i = 0;
            if (rest[0] == '>' || rest[0] == '<' || rest[0] == '=')
            {
                op = rest[0];
                i = 1;
                if (i < rest.Length && rest[i] == '=') { i++; }
            }

            if (!int.TryParse(rest.Substring(i), out var n)) return false;
            if (!isMonster || value < 0) { ok = false; return true; }
            var eq = i > 1 && rest[1] == '=';
            ok = op switch
            {
                '>' => eq ? value >= n : value > n,
                '<' => eq ? value <= n : value < n,
                _ => value == n
            };
            return true;
        }

        static bool Contains(string hay, string needle) =>
            !string.IsNullOrEmpty(hay) &&
            !string.IsNullOrEmpty(needle) &&
            hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Lower is better: name prefix, name hit, archetype, everything else.</summary>
        static int SearchRank(string q, int id, CardDef def)
        {
            if (string.IsNullOrWhiteSpace(q) || def == null) return 8;
            var first = q.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var t = first.Length > 0 ? first[0] : q;
            if (!string.IsNullOrEmpty(def.name) &&
                def.name.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (Contains(def.name, t)) return 1;
            if (Contains(def.archetype, t)) return 2;
            if (id.ToString() == t) return 3;
            if (Contains(def.type, t) || Contains(def.race, t) || Contains(def.attribute, t))
                return 4;
            return 6;
        }

        static bool PassArchetype(State st, CardDef def)
        {
            var arch = (st.Archetype ?? "").Trim();
            if (string.IsNullOrEmpty(arch)) return true;
            return def?.archetype != null &&
                   def.archetype.IndexOf(arch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool PassFilter(State st, CardDef def)
        {
            if (def == null) return st.Filter == Filter.All && !HasAdvancedFilters(st);
            switch (st.Filter)
            {
                case Filter.Mon:
                    if (!def.IsMonster || def.IsExtraDeck) return false;
                    break;
                case Filter.Spell:
                    if (!def.IsSpell) return false;
                    break;
                case Filter.Trap:
                    if (!def.IsTrap) return false;
                    break;
                case Filter.Extra:
                    if (!def.IsExtraDeck) return false;
                    break;
            }

            if (!PassKind(st.Kind, def)) return false;

            if (!string.IsNullOrEmpty(st.Attribute))
            {
                if (!def.IsMonster) return false;
                if (string.IsNullOrEmpty(def.attribute) ||
                    !def.attribute.Equals(st.Attribute, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrEmpty(st.MonsterType))
            {
                if (!def.IsMonster) return false;
                if (string.IsNullOrEmpty(def.race) ||
                    !def.race.Equals(st.MonsterType, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (st.Level > 0)
            {
                if (!def.IsMonster) return false;
                if (st.Level >= 9)
                {
                    if (def.level < 9) return false;
                }
                else if (def.level != st.Level) return false;
            }

            if (!PassStatBand(st.AtkBand, def.atk, def.IsMonster)) return false;
            if (!PassStatBand(st.DefBand, def.def, def.IsMonster)) return false;

            if (!string.IsNullOrEmpty(st.SpellKind))
            {
                if (!def.IsSpell) return false;
                if (!KindEquals(def.race, st.SpellKind)) return false;
            }

            if (!string.IsNullOrEmpty(st.TrapKind))
            {
                if (!def.IsTrap) return false;
                if (!KindEquals(def.race, st.TrapKind)) return false;
            }

            return true;
        }

        static bool PassKind(KindFilter kind, CardDef def)
        {
            if (kind == KindFilter.Any) return true;
            if (def == null || !def.IsMonster) return false;
            return kind switch
            {
                KindFilter.Normal => def.IsNormalMonster,
                KindFilter.Effect => def.IsEffectMonster && !def.IsExtraDeck,
                KindFilter.Fusion => TypeHas(def, "Fusion"),
                KindFilter.Ritual => TypeHas(def, "Ritual"),
                KindFilter.Flip => TypeHas(def, "Flip"),
                KindFilter.Spirit => TypeHas(def, "Spirit"),
                KindFilter.Union => TypeHas(def, "Union"),
                KindFilter.Toon => TypeHas(def, "Toon"),
                KindFilter.Tuner => TypeHas(def, "Tuner"),
                KindFilter.Synchro => TypeHas(def, "Synchro"),
                KindFilter.Xyz => TypeHas(def, "XYZ") || TypeHas(def, "Xyz"),
                KindFilter.Link => TypeHas(def, "Link"),
                KindFilter.Pendulum => TypeHas(def, "Pendulum"),
                _ => true
            };
        }

        static bool TypeHas(CardDef def, string token) =>
            Contains(def.type, token) || Contains(def.frameType, token);

        static bool PassStatBand(StatBand band, int value, bool isMonster)
        {
            if (band == StatBand.Any) return true;
            if (!isMonster || value < 0) return false;
            return band switch
            {
                StatBand.Lt1000 => value < 1000,
                StatBand.R1000 => value >= 1000 && value < 1500,
                StatBand.R1500 => value >= 1500 && value < 2000,
                StatBand.R2000 => value >= 2000 && value < 2500,
                StatBand.Ge2500 => value >= 2500,
                _ => true
            };
        }

        static HashSet<int> DeckIdSet(State st)
        {
            var set = new HashSet<int>();
            if (st.Main != null) foreach (var id in st.Main) set.Add(id);
            if (st.Extra != null) foreach (var id in st.Extra) set.Add(id);
            if (st.Side != null) foreach (var id in st.Side) set.Add(id);
            return set;
        }

        static bool PassInDeck(State st, int id, HashSet<int> deckIds)
        {
            if (st.InDeck == InDeckFilter.Any) return true;
            var inDeck = deckIds != null && deckIds.Contains(id);
            return st.InDeck == InDeckFilter.InDeck ? inDeck : !inDeck;
        }

        static bool KindEquals(string race, string want) =>
            !string.IsNullOrEmpty(race) &&
            race.Equals(want, StringComparison.OrdinalIgnoreCase);

        static bool HasAdvancedFilters(State st) =>
            st.Kind != KindFilter.Any ||
            st.Level > 0 ||
            st.AtkBand != StatBand.Any ||
            st.DefBand != StatBand.Any ||
            st.InDeck != InDeckFilter.Any ||
            !string.IsNullOrEmpty(st.MonsterType) ||
            !string.IsNullOrEmpty(st.Attribute) ||
            !string.IsNullOrEmpty(st.SpellKind) ||
            !string.IsNullOrEmpty(st.TrapKind) ||
            !string.IsNullOrEmpty(st.Archetype);

        static List<string> CollectArchetypes(State st)
        {
            // Prefer archetypes the player actually owns (cleaner than full DB dump)
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (st.Inv?.storageBoxes != null && st.Db != null)
            {
                foreach (var box in st.Inv.storageBoxes)
                {
                    if (box?.stacks == null) continue;
                    foreach (var s in box.stacks)
                    {
                        if (s == null || s.cardId <= 0) continue;
                        var d = st.Db.Get(s.cardId);
                        if (d != null && !string.IsNullOrWhiteSpace(d.archetype))
                            set.Add(d.archetype.Trim());
                    }
                }
            }

            if (set.Count == 0 && st.Db != null)
            {
                foreach (var d in st.Db.GetAllCards())
                    if (d != null && !string.IsNullOrWhiteSpace(d.archetype))
                        set.Add(d.archetype.Trim());
            }

            return set.OrderBy(a => a).ToList();
        }

        // ── Filter bar + tray (faceted, mobile-friendly) ─────────────────────

        static void BuildFilterBar(RectTransform phase, State st, bool wide, float fromTop, float height)
        {
            // Search + FILTER + sort — stay over the board, never the card list.
            var searchX1 = wide ? 0.38f : 0.56f;
            var icoX0 = wide ? 0.39f : 0.575f;
            var icoX1 = wide ? 0.615f : 1f;
            var searchGo = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(InputField));
            searchGo.transform.SetParent(phase, false);
            PinTop(searchGo.GetComponent<RectTransform>(), 0f, searchX1, fromTop, height);
            var sBg = searchGo.GetComponent<Image>();
            var searchPlate = ImagineAssets.PanelHolo() ?? ImagineAssets.InputField();
            if (searchPlate != null)
            {
                sBg.sprite = searchPlate;
                sBg.type = searchPlate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                sBg.color = Color.white;
            }
            else
            {
                StyleGlass(sBg, new Color(0.04f, 0.07f, 0.12f, 0.82f), new Color(0.30f, 0.70f, 0.88f, 0.28f));
            }

            var sText = Label(searchGo.transform, st.Search ?? "", 13, Color.white, TextAnchor.MiddleLeft);
            FloatingPanel.Place(sText.rectTransform, 0.03f, 0.08f, 0.84f, 0.92f);
            sText.raycastTarget = true;
            sText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = Label(searchGo.transform, "Search  name  lv7  atk>2000", 12,
                new Color(0.58f, 0.64f, 0.70f, 0.80f), TextAnchor.MiddleLeft);
            FloatingPanel.Place(ph.rectTransform, 0.03f, 0.08f, 0.84f, 0.92f);
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;
            var field = searchGo.GetComponent<InputField>();
            field.textComponent = sText;
            field.placeholder = ph;
            field.text = st.Search ?? "";

            void ApplySearch(string v)
            {
                var next = v ?? "";
                if (next == (st.Search ?? "")) return;
                st.Search = next;
                if (st.PoolGrid != null) FillCardGrids(st, resetScroll: true);
                else st.Rebuild();
            }

            field.onEndEdit.AddListener(ApplySearch);
            var live = searchGo.AddComponent<SearchLive>();
            live.Bind(field, ApplySearch);

            var clear = DeckChromeButton(searchGo.transform, "×", () =>
            {
                field.text = "";
                ApplySearch("");
            }, new Color(0.14f, 0.12f, 0.14f, 0.90f), compact: true);
            FloatingPanel.Place(clear.GetComponent<RectTransform>(), 0.86f, 0.10f, 0.98f, 0.90f);

            var icoSpan = Mathf.Max(0.12f, icoX1 - icoX0);
            float sx = icoX0;
            var sw = icoSpan / 8.2f;
            void Ico(Filter f, Sprite spr)
            {
                var on = st.Filter == f;
                var b = IconButton(phase, spr, on, () =>
                {
                    st.Filter = f;
                    FreeUiKit.PlaySelect();
                    st.Rebuild();
                });
                PinTop(b.GetComponent<RectTransform>(), sx, sx + sw, fromTop, height);
                sx += sw;
            }

            Ico(Filter.All, ImagineAssets.IconFilterAll());
            Ico(Filter.Mon, ImagineAssets.IconFilterMonster());
            Ico(Filter.Spell, ImagineAssets.IconFilterSpell());
            Ico(Filter.Trap, ImagineAssets.IconFilterTrap());
            Ico(Filter.Extra, ImagineAssets.IconFilterExtra());

            var facetOn = HasAdvancedFilters(st);
            var facet = IconButton(phase, ImagineAssets.IconFilterFacets(), facetOn, () => OpenFilterTray(phase, st));
            PinTop(facet.GetComponent<RectTransform>(), sx, sx + sw, fromTop, height);
            sx += sw;

            var sortI = IconButton(phase, ImagineAssets.IconFilterSort(), false, () =>
            {
                st.Sort = st.Sort switch
                {
                    SortMode.Name => SortMode.Level,
                    SortMode.Level => SortMode.Atk,
                    SortMode.Atk => SortMode.Def,
                    SortMode.Def => SortMode.Type,
                    _ => SortMode.Name
                };
                FreeUiKit.PlaySelect();
                st.Rebuild();
            });
            PinTop(sortI.GetComponent<RectTransform>(), sx, sx + sw, fromTop, height);
        }

        static Button SectionDeckIcon(Transform parent, Sprite spr, int count, bool on,
            Color countColor, Action click)
        {
            var go = new GameObject("Sec", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = on ? GlassOn : new Color(1f, 1f, 1f, 0.02f);
            bg.raycastTarget = true;
            if (on)
            {
                var ol = go.AddComponent<Outline>();
                ol.effectColor = new Color(0.45f, 0.90f, 1f, 0.55f);
                ol.effectDistance = new Vector2(1.6f, -1.6f);
                ol.useGraphicAlpha = false;
            }

            var artGo = new GameObject("Art", typeof(RectTransform), typeof(Image));
            artGo.transform.SetParent(go.transform, false);
            var art = artGo.GetComponent<Image>();
            art.sprite = spr ?? UiFoundation.WhiteSprite();
            art.preserveAspect = true;
            art.color = on ? Color.white : new Color(0.72f, 0.76f, 0.82f, 0.88f);
            art.raycastTarget = false;
            FloatingPanel.Place(art.rectTransform, 0.08f, 0.26f, 0.92f, 0.96f);

            var n = Label(go.transform, count.ToString(), 10, countColor, TextAnchor.MiddleCenter);
            n.font = FreeUiKit.UiFont() ?? WrldzType.Body() ?? UiFoundation.BuiltinFont();
            n.fontStyle = FontStyle.Bold;
            n.fontSize = 18;
            n.resizeTextForBestFit = false;
            n.horizontalOverflow = HorizontalWrapMode.Overflow;
            n.verticalOverflow = VerticalWrapMode.Overflow;
            n.alignment = TextAnchor.MiddleCenter;
            n.color = countColor;
            FloatingPanel.Place(n.rectTransform, 0.04f, 0.00f, 0.96f, 0.28f);

            var b = go.GetComponent<Button>();
            b.targetGraphic = bg;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                click?.Invoke();
            });
            return b;
        }

        static Button IconButton(Transform parent, Sprite spr, bool on, Action click)
        {
            var go = new GameObject("Ico", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = spr ?? UiFoundation.WhiteSprite();
            img.preserveAspect = true;
            img.color = on ? Color.white : new Color(0.72f, 0.76f, 0.82f, 0.82f);
            img.raycastTarget = true;
            if (on)
            {
                var ol = go.AddComponent<Outline>();
                ol.effectColor = new Color(0.55f, 0.90f, 1f, 0.55f);
                ol.effectDistance = new Vector2(1.4f, -1.4f);
                ol.useGraphicAlpha = false;
            }

            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                click?.Invoke();
            });
            return b;
        }

        static string SortLabel(SortMode s) => s switch
        {
            SortMode.Level => "LV",
            SortMode.Atk => "ATK",
            SortMode.Def => "DEF",
            SortMode.Type => "TYPE",
            _ => "A–Z"
        };

        static string KindLabel(KindFilter kind)
        {
            for (var i = 0; i < KindOptions.Length; i++)
                if (KindOptions[i].Kind == kind)
                    return KindOptions[i].Lab;
            return "KIND";
        }

        static string StatBandLabel(StatBand band, string prefix)
        {
            for (var i = 0; i < StatBandOptions.Length; i++)
                if (StatBandOptions[i].Band == band)
                    return prefix + StatBandOptions[i].Lab;
            return prefix;
        }

        static void ClearAdvancedFilters(State st)
        {
            st.Kind = KindFilter.Any;
            st.Level = 0;
            st.MonsterType = "";
            st.Attribute = "";
            st.SpellKind = "";
            st.TrapKind = "";
            st.Archetype = "";
            st.AtkBand = StatBand.Any;
            st.DefBand = StatBand.Any;
            st.InDeck = InDeckFilter.Any;
        }

        static void BuildActiveFilterChips(RectTransform phase, State st)
        {
            var hasSearch = !string.IsNullOrWhiteSpace(st.Search);
            var hasType = st.Filter != Filter.All;
            var hasAdv = HasAdvancedFilters(st);
            if (!hasSearch && !hasType && !hasAdv)
                return;

            float x = 0f;
            void Chip(string text, Action clear)
            {
                var w = Mathf.Clamp(0.11f + text.Length * 0.011f, 0.14f, 0.30f);
                if (x + w > 0.86f) return;
                var go = DeckChromeButton(phase, text + " ×", () =>
                {
                    FreeUiKit.PlayClick();
                    clear();
                    st.Rebuild();
                }, new Color(0.16f, 0.18f, 0.24f, 0.95f), compact: true);
                FloatingPanel.Place(go.GetComponent<RectTransform>(), x, ChipY0, x + w, ChipY1);
                x += w + 0.010f;
            }

            if (hasType)
                Chip(TypeLabel(st.Filter), () => st.Filter = Filter.All);
            if (st.Kind != KindFilter.Any)
                Chip(KindLabel(st.Kind), () => st.Kind = KindFilter.Any);
            if (!string.IsNullOrEmpty(st.Attribute))
            {
                var a = st.Attribute;
                Chip(AbbrOf(AttributeOptions, AttributeAbbr, a), () => st.Attribute = "");
            }
            if (st.Level > 0)
                Chip(st.Level >= 9 ? "LV9+" : "LV" + st.Level, () => st.Level = 0);
            if (st.AtkBand != StatBand.Any)
                Chip(StatBandLabel(st.AtkBand, "ATK"), () => st.AtkBand = StatBand.Any);
            if (st.DefBand != StatBand.Any)
                Chip(StatBandLabel(st.DefBand, "DEF"), () => st.DefBand = StatBand.Any);
            if (!string.IsNullOrEmpty(st.MonsterType))
            {
                var r = st.MonsterType;
                Chip(AbbrOf(MonsterTypeOptions, MonsterTypeAbbr, r), () => st.MonsterType = "");
            }
            if (!string.IsNullOrEmpty(st.SpellKind))
            {
                var s = st.SpellKind;
                Chip("S:" + AbbrOf(SpellKindOptions, SpellKindAbbr, s), () => st.SpellKind = "");
            }
            if (!string.IsNullOrEmpty(st.TrapKind))
            {
                var t = st.TrapKind;
                Chip("T:" + AbbrOf(TrapKindOptions, TrapKindAbbr, t), () => st.TrapKind = "");
            }
            if (st.InDeck == InDeckFilter.InDeck)
                Chip("IN DECK", () => st.InDeck = InDeckFilter.Any);
            if (st.InDeck == InDeckFilter.NotInDeck)
                Chip("NOT IN", () => st.InDeck = InDeckFilter.Any);
            if (!string.IsNullOrEmpty(st.Archetype))
            {
                var a = st.Archetype;
                Chip(a.Length > 12 ? a.Substring(0, 11) + "…" : a, () => st.Archetype = "");
            }
            if (hasSearch)
            {
                var q = st.Search.Trim();
                Chip(q.Length > 10 ? "\"" + q.Substring(0, 9) + "…\"" : "\"" + q + "\"",
                    () => st.Search = "");
            }

            var clearAll = DeckChromeButton(phase, "CLEAR", () =>
            {
                st.Search = "";
                st.Filter = Filter.All;
                st.Scope = FilterScope.Both;
                ClearAdvancedFilters(st);
                FreeUiKit.PlayConfirm();
                st.Rebuild();
            }, new Color(0.16f, 0.18f, 0.24f, 0.95f), compact: true);
            FloatingPanel.Place(clearAll.GetComponent<RectTransform>(), 0.87f, ChipY0, 1f, ChipY1);
        }

        static string TypeLabel(Filter f) => f switch
        {
            Filter.Mon => "MON",
            Filter.Spell => "SPL",
            Filter.Trap => "TRP",
            Filter.Extra => "EX",
            _ => "ALL"
        };

        static string AbbrOf(string[] full, string[] abbr, string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            for (var i = 0; i < full.Length && i < abbr.Length; i++)
                if (full[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                    return abbr[i];
            return value.Length > 6 ? value.Substring(0, 6) : value;
        }

        /// <summary>
        /// Faceted filter tray: apply-to (collection/deck), kind, attribute, level,
        /// ATK/DEF bands, monster type, spell/trap subtype, owned-in-deck, archetype.
        /// Stays open while picking; DONE rebuilds the grids.
        /// </summary>
        static void OpenFilterTray(RectTransform phase, State st)
        {
            var dim = new GameObject("FilterTrayDim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(phase, false);
            FloatingPanel.Stretch(dim.GetComponent<RectTransform>());
            var dimImg = dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = new Color(0f, 0f, 0f, 0.55f);
            dimImg.raycastTarget = true;

            var sheet = new GameObject("FilterTray", typeof(RectTransform), typeof(Image));
            sheet.transform.SetParent(phase, false);
            FloatingPanel.Place(sheet.GetComponent<RectTransform>(), 0.03f, 0.06f, 0.97f, ChromeToolY0 - 0.010f);
            var sImg = sheet.GetComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(0.06f, 0.08f, 0.12f, 0.96f);
            sImg.raycastTarget = true;

            var title = Label(sheet.transform, "FILTERS", 16, DuelystUi.GoldHot, TextAnchor.MiddleLeft);
            FloatingPanel.Place(title.rectTransform, 0.04f, 0.91f, 0.58f, 0.985f);

            void CloseTray()
            {
                if (dim != null) UnityEngine.Object.Destroy(dim);
            }

            dim.GetComponent<Button>().onClick.AddListener(() =>
            {
                CloseTray();
                st.Rebuild();
            });

            var close = DeckChromeButton(sheet.transform, "DONE", () =>
            {
                CloseTray();
                st.Rebuild();
            }, new Color(0.12f, 0.42f, 0.52f, 0.50f), compact: true);
            FloatingPanel.Place(close.GetComponent<RectTransform>(), 0.72f, 0.91f, 0.96f, 0.985f);

            var scroll = new GameObject("FilterScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(sheet.transform, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.98f, 0.90f);
            var scImg = scroll.GetComponent<Image>();
            scImg.sprite = UiFoundation.WhiteSprite();
            scImg.color = new Color(1, 1, 1, 0.02f);

            var vp = new GameObject("VP", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            vp.transform.SetParent(scroll.transform, false);
            FloatingPanel.Stretch(vp.GetComponent<RectTransform>(), 2f);
            vp.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            vp.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);

            var content = new GameObject("C", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(vp.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(4, 4, 2, 8);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sc = scroll.GetComponent<ScrollRect>();
            sc.viewport = vp.GetComponent<RectTransform>();
            sc.content = crt;
            sc.horizontal = false;
            sc.vertical = true;
            sc.movementType = ScrollRect.MovementType.Clamped;
            sc.scrollSensitivity = 70f;

            void RefreshTray()
            {
                CloseTray();
                OpenFilterTray(phase, st);
            }

            void ApplyMonsterFacets()
            {
                if (st.Filter == Filter.All || st.Filter == Filter.Spell || st.Filter == Filter.Trap)
                    st.Filter = Filter.Mon;
                st.SpellKind = "";
                st.TrapKind = "";
            }

            FacetHeader(content.transform, "KIND");
            var kindLabs = new string[KindOptions.Length];
            for (var k = 0; k < KindOptions.Length; k++)
                kindLabs[k] = KindOptions[k].Lab;
            FacetChips(content.transform, kindLabs,
                i => st.Kind == KindOptions[i].Kind,
                i =>
                {
                    st.Kind = KindOptions[i].Kind;
                    if (st.Kind != KindFilter.Any)
                    {
                        if (st.Kind is KindFilter.Fusion or KindFilter.Synchro
                            or KindFilter.Xyz or KindFilter.Link)
                            st.Filter = Filter.Extra;
                        else
                            ApplyMonsterFacets();
                        st.SpellKind = "";
                        st.TrapKind = "";
                    }
                    RefreshTray();
                });

            FacetHeader(content.transform, "ATTRIBUTE");
            FacetIconChips(content.transform, WithAny(AttributeAbbr), AttrSprites(),
                i => i == 0 ? string.IsNullOrEmpty(st.Attribute)
                    : st.Attribute.Equals(AttributeOptions[i - 1], StringComparison.OrdinalIgnoreCase),
                i =>
                {
                    st.Attribute = i == 0 ? "" : AttributeOptions[i - 1];
                    if (i > 0)
                    {
                        if (st.Filter == Filter.All) st.Filter = Filter.Mon;
                        st.SpellKind = "";
                        st.TrapKind = "";
                    }
                    RefreshTray();
                }, cols: 8, showLabel: false);

            FacetHeader(content.transform, "LV");
            var levelLabs = new[] { "ANY", "1", "2", "3", "4", "5", "6", "7", "8", "9+" };
            FacetChips(content.transform, levelLabs,
                i => i == 0 ? st.Level == 0 : (i == 9 ? st.Level >= 9 : st.Level == i),
                i =>
                {
                    st.Level = i == 0 ? 0 : (i == 9 ? 9 : i);
                    if (st.Level > 0)
                    {
                        if (st.Filter == Filter.All) st.Filter = Filter.Mon;
                        st.SpellKind = "";
                        st.TrapKind = "";
                    }
                    RefreshTray();
                });

            FacetHeader(content.transform, "ATK");
            var atkLabs = new string[StatBandOptions.Length];
            for (var a = 0; a < StatBandOptions.Length; a++)
                atkLabs[a] = StatBandOptions[a].Lab;
            FacetChips(content.transform, atkLabs,
                i => st.AtkBand == StatBandOptions[i].Band,
                i =>
                {
                    st.AtkBand = StatBandOptions[i].Band;
                    if (st.AtkBand != StatBand.Any) ApplyMonsterFacets();
                    RefreshTray();
                });

            FacetHeader(content.transform, "DEF");
            var defLabs = new string[StatBandOptions.Length];
            for (var d = 0; d < StatBandOptions.Length; d++)
                defLabs[d] = StatBandOptions[d].Lab;
            FacetChips(content.transform, defLabs,
                i => st.DefBand == StatBandOptions[i].Band,
                i =>
                {
                    st.DefBand = StatBandOptions[i].Band;
                    if (st.DefBand != StatBand.Any) ApplyMonsterFacets();
                    RefreshTray();
                });

            FacetHeader(content.transform, "MONSTER TYPE");
            FacetIconChips(content.transform, WithAny(MonsterTypeAbbr), TypeSprites(),
                i => i == 0 ? string.IsNullOrEmpty(st.MonsterType)
                    : st.MonsterType.Equals(MonsterTypeOptions[i - 1], StringComparison.OrdinalIgnoreCase),
                i =>
                {
                    st.MonsterType = i == 0 ? "" : MonsterTypeOptions[i - 1];
                    if (i > 0)
                    {
                        if (st.Filter == Filter.All) st.Filter = Filter.Mon;
                        st.SpellKind = "";
                        st.TrapKind = "";
                    }
                    RefreshTray();
                }, cols: 7, showLabel: true);

            FacetHeader(content.transform, "SPELL");
            FacetIconChips(content.transform, WithAny(SpellKindAbbr), SpellSprites(),
                i => i == 0 ? string.IsNullOrEmpty(st.SpellKind)
                    : st.SpellKind.Equals(SpellKindOptions[i - 1], StringComparison.OrdinalIgnoreCase),
                i =>
                {
                    st.SpellKind = i == 0 ? "" : SpellKindOptions[i - 1];
                    if (i > 0)
                    {
                        st.Filter = Filter.Spell;
                        st.Kind = KindFilter.Any;
                        st.MonsterType = "";
                        st.Attribute = "";
                        st.Level = 0;
                        st.AtkBand = StatBand.Any;
                        st.DefBand = StatBand.Any;
                        st.TrapKind = "";
                    }
                    RefreshTray();
                }, cols: 7, showLabel: true);

            FacetHeader(content.transform, "TRAP");
            FacetIconChips(content.transform, WithAny(TrapKindAbbr), TrapSprites(),
                i => i == 0 ? string.IsNullOrEmpty(st.TrapKind)
                    : st.TrapKind.Equals(TrapKindOptions[i - 1], StringComparison.OrdinalIgnoreCase),
                i =>
                {
                    st.TrapKind = i == 0 ? "" : TrapKindOptions[i - 1];
                    if (i > 0)
                    {
                        st.Filter = Filter.Trap;
                        st.Kind = KindFilter.Any;
                        st.MonsterType = "";
                        st.Attribute = "";
                        st.Level = 0;
                        st.AtkBand = StatBand.Any;
                        st.DefBand = StatBand.Any;
                        st.SpellKind = "";
                    }
                    RefreshTray();
                }, cols: 4, showLabel: true);

            FacetHeader(content.transform, "OWNED");
            FacetChips(content.transform, new[] { "ANY", "IN DECK", "NOT IN" },
                i => (int)st.InDeck == i,
                i =>
                {
                    st.InDeck = (InDeckFilter)i;
                    if (st.InDeck != InDeckFilter.Any && st.Scope == FilterScope.Deck)
                        st.Scope = FilterScope.Collection;
                    RefreshTray();
                });

            FacetHeader(content.transform, "ARCH");
            var arches = CollectArchetypes(st);
            var archLabs = new List<string> { "ANY" };
            foreach (var a in arches.Take(32))
                archLabs.Add(a.Length > 8 ? a.Substring(0, 8) : a);
            FacetChips(content.transform, archLabs.ToArray(),
                i => i == 0
                    ? string.IsNullOrEmpty(st.Archetype)
                    : string.Equals(st.Archetype, arches[i - 1], StringComparison.OrdinalIgnoreCase),
                i =>
                {
                    st.Archetype = i == 0 ? "" : arches[i - 1];
                    RefreshTray();
                });
        }

        static string[] WithAny(string[] rest)
        {
            var a = new string[rest.Length + 1];
            a[0] = "ANY";
            Array.Copy(rest, 0, a, 1, rest.Length);
            return a;
        }

        static Sprite[] AttrSprites()
        {
            var a = new Sprite[AttributeOptions.Length + 1];
            for (var i = 0; i < AttributeOptions.Length; i++)
                a[i + 1] = ImagineAssets.YgoAttribute(AttributeOptions[i]);
            return a;
        }

        static Sprite[] TypeSprites()
        {
            var a = new Sprite[MonsterTypeOptions.Length + 1];
            for (var i = 0; i < MonsterTypeOptions.Length; i++)
                a[i + 1] = ImagineAssets.YgoMonsterType(MonsterTypeOptions[i]);
            return a;
        }

        static Sprite[] SpellSprites()
        {
            var a = new Sprite[SpellKindOptions.Length + 1];
            for (var i = 0; i < SpellKindOptions.Length; i++)
                a[i + 1] = ImagineAssets.YgoSpellTrapKind(SpellKindOptions[i]);
            return a;
        }

        static Sprite[] TrapSprites()
        {
            var a = new Sprite[TrapKindOptions.Length + 1];
            for (var i = 0; i < TrapKindOptions.Length; i++)
                a[i + 1] = ImagineAssets.YgoSpellTrapKind(TrapKindOptions[i]);
            return a;
        }

        static void FacetHeader(Transform parent, string text)
        {
            var go = new GameObject("H_" + text, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 26f;
            le.preferredHeight = 26f;
            le.flexibleWidth = 1f;
            var t = go.GetComponent<Text>();
            t.font = WrldzType.Body() ?? UiFoundation.BuiltinFont();
            t.fontSize = 15;
            t.fontStyle = FontStyle.Bold;
            t.text = text;
            t.color = DuelystUi.Cyan;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
        }

        static void FacetChips(Transform parent, string[] labels, Func<int, bool> isOn, Action<int> pick)
        {
            var cols = labels.Length <= 4 ? Mathf.Max(labels.Length, 1) : 6;
            var rows = Mathf.CeilToInt(labels.Length / (float)cols);
            var go = new GameObject("Chips", typeof(RectTransform), typeof(GridLayoutGroup),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<GridLayoutGroup>();
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = cols;
            g.cellSize = new Vector2(cols <= 3 ? 168f : 96f, 34f);
            g.spacing = new Vector2(4f, 4f);
            g.childAlignment = TextAnchor.UpperLeft;
            g.padding = new RectOffset(0, 0, 0, 2);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = rows * 38f;
            le.preferredHeight = le.minHeight;
            le.flexibleWidth = 1f;

            for (var i = 0; i < labels.Length; i++)
            {
                var idx = i;
                var on = isOn(idx);
                var btn = DeckChromeButton(go.transform, labels[idx], () =>
                {
                    FreeUiKit.PlaySelect();
                    pick(idx);
                }, on
                    ? new Color(0.12f, 0.42f, 0.52f, 0.70f)
                    : new Color(0.10f, 0.12f, 0.18f, 0.42f), compact: true);
                btn.GetComponent<LayoutElement>().minHeight = 32f;
                btn.GetComponent<LayoutElement>().preferredHeight = 32f;
                btn.GetComponent<LayoutElement>().flexibleWidth = 0f;
            }
        }

        static void PoolChip(State st, Transform host, PoolRow row)
        {
            var go = ArtChip(st, host, row.Id, row.OnHand + row.AtHome, row.Id == st.SelectedId);
            if (row.HomeOnly || row.OnHand <= 0)
            {
                var veil = new GameObject("V", typeof(RectTransform), typeof(Image));
                veil.transform.SetParent(go.transform, false);
                FloatingPanel.Stretch(veil.GetComponent<RectTransform>());
                veil.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
                veil.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 0.55f);
                veil.GetComponent<Image>().raycastTarget = false;
            }

            BindChipInteract(go, st, row.Id, fromPool: !(row.HomeOnly || row.OnHand <= 0),
                Section.Main, st.PoolScroll);
        }

        static void FacetIconChips(Transform parent, string[] labels, Sprite[] icons,
            Func<int, bool> isOn, Action<int> pick, int cols, bool showLabel)
        {
            cols = Mathf.Clamp(cols, 2, 8);
            var rows = Mathf.CeilToInt(labels.Length / (float)cols);
            var cellW = 76f;
            var cellH = showLabel ? 82f : 70f;
            var go = new GameObject("IconChips", typeof(RectTransform), typeof(GridLayoutGroup),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<GridLayoutGroup>();
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = cols;
            g.cellSize = new Vector2(cellW, cellH);
            g.spacing = new Vector2(6f, 6f);
            g.childAlignment = TextAnchor.UpperLeft;
            g.padding = new RectOffset(0, 0, 2, 6);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = rows * (cellH + 6f);
            le.preferredHeight = le.minHeight;
            le.flexibleWidth = 1f;

            for (var i = 0; i < labels.Length; i++)
            {
                var idx = i;
                var on = isOn(idx);
                var spr = icons != null && idx < icons.Length ? icons[idx] : null;
                var cell = new GameObject("Ico_" + labels[idx], typeof(RectTransform), typeof(Image),
                    typeof(Button), typeof(LayoutElement));
                cell.transform.SetParent(go.transform, false);
                var bg = cell.GetComponent<Image>();
                bg.sprite = UiFoundation.WhiteSprite();
                bg.color = on
                    ? new Color(0.18f, 0.55f, 0.68f, 0.55f)
                    : new Color(0.08f, 0.10f, 0.14f, 0.35f);
                bg.raycastTarget = true;
                var b = cell.GetComponent<Button>();
                b.targetGraphic = bg;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() =>
                {
                    FreeUiKit.PlaySelect();
                    pick(idx);
                });

                if (spr != null)
                {
                    var ico = new GameObject("G", typeof(RectTransform), typeof(Image));
                    ico.transform.SetParent(cell.transform, false);
                    var ir = ico.GetComponent<RectTransform>();
                    ir.anchorMin = new Vector2(0.12f, showLabel ? 0.30f : 0.10f);
                    ir.anchorMax = new Vector2(0.88f, 0.94f);
                    ir.offsetMin = Vector2.zero;
                    ir.offsetMax = Vector2.zero;
                    var ii = ico.GetComponent<Image>();
                    ii.sprite = spr;
                    ii.preserveAspect = true;
                    ii.color = Color.white;
                    ii.raycastTarget = false;
                }

                if (showLabel || spr == null)
                {
                    var lab = Label(cell.transform, labels[idx], 11, Color.white, TextAnchor.MiddleCenter);
                    lab.resizeTextForBestFit = true;
                    lab.resizeTextMinSize = 8;
                    lab.resizeTextMaxSize = 12;
                    lab.horizontalOverflow = HorizontalWrapMode.Overflow;
                    FloatingPanel.Place(lab.rectTransform, 0.04f,
                        spr == null ? 0.18f : 0.02f, 0.96f, spr == null ? 0.82f : 0.30f);
                }
            }
        }

        static void DeckChip(State st, Transform host, int id, int qty)
        {
            var go = ArtChip(st, host, id, qty, id == st.SelectedId);
            BindChipInteract(go, st, id, fromPool: false, st.Section, st.DeckScroll);
        }

        static void BindChipInteract(GameObject go, State st, int id, bool fromPool,
            Section section, ScrollRect scroll)
        {
            var btn = go.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                if (!fromPool) st.Section = section;
                if (st.Inspect != null && st.Inspect.activeSelf && st.SelectedId == id)
                {
                    HideInspect(st);
                    return;
                }

                ShowInspect(st, id);
            });

            var pass = go.GetComponent<DeckChipInteract>() ?? go.AddComponent<DeckChipInteract>();
            pass.Bind(st, id, fromPool, section, scroll);
        }

        static GameObject ArtChip(State st, Transform host, int id, int qty, bool selected)
        {
            var go = new GameObject("C" + id, typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = ChipW;
            le.preferredHeight = ChipH;
            le.minWidth = ChipW;
            le.minHeight = ChipH;
            var face = go.GetComponent<Image>();
            face.sprite = UiFoundation.WhiteSprite();
            face.color = selected
                ? new Color(0.55f, 0.90f, 1f, 0.18f)
                : new Color(0.08f, 0.09f, 0.12f, 0.12f);
            face.raycastTarget = true;

            var artGo = new GameObject("A", typeof(RectTransform), typeof(Image));
            artGo.transform.SetParent(go.transform, false);
            var artRt = artGo.GetComponent<RectTransform>();
            artRt.anchorMin = Vector2.zero;
            artRt.anchorMax = Vector2.one;
            artRt.offsetMin = new Vector2(2, 2);
            artRt.offsetMax = new Vector2(-2, -2);
            var art = artGo.GetComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;
            art.sprite = YgoCardFrames.CardBack() ?? UiFoundation.WhiteSprite();
            art.color = Color.white;
            var lazy = artGo.AddComponent<DeferredCardArt>();
            lazy.Bind(st.Db, id, art);

            if (qty > 1)
                CopiesStamp(go.transform, qty);
            BanlistPip(go.transform, id);

            return go;
        }

        static void BanlistPip(Transform card, int id)
        {
            var status = OfficialDataSources.StatusOf(id);
            Sprite spr = status switch
            {
                BanlistStatus.Forbidden => ImagineAssets.IconLimitForbidden(),
                BanlistStatus.Limited => ImagineAssets.IconLimitLimited(),
                BanlistStatus.SemiLimited => ImagineAssets.IconLimitSemi(),
                _ => null
            };
            if (spr == null) return;
            var badge = new GameObject("Ban", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(card, false);
            badge.transform.SetAsLastSibling();
            FloatingPanel.Place(badge.GetComponent<RectTransform>(), 0.02f, 0.72f, 0.34f, 0.98f);
            var img = badge.GetComponent<Image>();
            img.sprite = spr;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = Color.white;
        }

        /// <summary>Corner stamp on the card face: X2 / X3 so duplicate copies read at a glance.</summary>
        static void CopiesStamp(Transform card, int qty)
        {
            var n = Mathf.Clamp(qty, 2, 99);
            var badge = new GameObject("Qty", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(card, false);
            badge.transform.SetAsLastSibling();
            FloatingPanel.Place(badge.GetComponent<RectTransform>(), 0.50f, 0.02f, 0.97f, 0.26f);
            var plate = badge.GetComponent<Image>();
            plate.sprite = UiFoundation.WhiteSprite();
            plate.color = new Color(0.04f, 0.05f, 0.07f, 0.88f);
            plate.raycastTarget = false;

            var tGo = new GameObject("T", typeof(RectTransform), typeof(Text), typeof(Outline));
            tGo.transform.SetParent(badge.transform, false);
            FloatingPanel.Stretch(tGo.GetComponent<RectTransform>(), 1f);
            var t = tGo.GetComponent<Text>();
            t.font = WrldzType.Display() ?? WrldzType.Body() ?? UiFoundation.BuiltinFont();
            t.fontSize = 16;
            t.fontStyle = FontStyle.Bold;
            t.text = "X" + n;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = false;
            var o = tGo.GetComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 1f);
            o.effectDistance = new Vector2(1.6f, -1.6f);
        }

        // ── Drag ─────────────────────────────────────────────────────────────

        static void AddDrag(GameObject go, State st, int cardId, bool fromPool)
        {
            var tr = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
            tr.triggers = new List<EventTrigger.Entry>();

            void E(EventTriggerType t, Action<PointerEventData> a)
            {
                var e = new EventTrigger.Entry { eventID = t };
                e.callback.AddListener(d =>
                {
                    if (d is PointerEventData p) a(p);
                });
                tr.triggers.Add(e);
            }

            E(EventTriggerType.BeginDrag, ped =>
            {
                st.DragId = cardId;
                st.DragFromPool = fromPool;
                st.SelectedId = cardId;
                if (st.Ghost != null) UnityEngine.Object.Destroy(st.Ghost);
                var g = new GameObject("G", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                g.transform.SetParent(st.DragLayer, false);
                var rt = g.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(ChipW * 1.2f, ChipH * 1.2f);
                var img = g.GetComponent<Image>();
                img.sprite = st.Db != null ? st.Db.GetArt(cardId) : null;
                if (img.sprite == null) img.sprite = YgoCardFrames.CardBack() ?? UiFoundation.WhiteSprite();
                img.preserveAspect = true;
                img.raycastTarget = false;
                g.GetComponent<CanvasGroup>().blocksRaycasts = false;
                g.GetComponent<CanvasGroup>().alpha = 0.88f;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    st.DragLayer as RectTransform, ped.position, ped.pressEventCamera, out var loc);
                rt.anchoredPosition = loc;
                st.Ghost = g;
            });

            E(EventTriggerType.Drag, ped =>
            {
                if (st.Ghost == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    st.DragLayer as RectTransform, ped.position, ped.pressEventCamera, out var loc);
                st.Ghost.GetComponent<RectTransform>().anchoredPosition = loc;
            });

            E(EventTriggerType.EndDrag, ped =>
            {
                if (st.Ghost != null)
                {
                    UnityEngine.Object.Destroy(st.Ghost);
                    st.Ghost = null;
                }

                // Vertical split: collection LEFT · deck RIGHT (drop across the gutter)
                var dropOnDeck = ped.position.x > Screen.width * 0.5f;
                if (st.DragFromPool && dropOnDeck)
                {
                    if (TryAdd(st, st.DragId, out var msg))
                    {
                        FreeUiKit.PlayConfirm();
                        Status(st, msg, true);
                        if (st.RefreshCards != null) st.RefreshCards();
                        else st.Rebuild();
                    }
                    else Status(st, msg, false);
                }
                else if (!st.DragFromPool && !dropOnDeck)
                {
                    if (TryRem(st, st.DragId, out var msg))
                    {
                        FreeUiKit.PlayClick();
                        Status(st, msg, true);
                        if (st.RefreshCards != null) st.RefreshCards();
                        else st.Rebuild();
                    }
                }

                st.DragId = 0;
            });
        }

        static bool TryAdd(State st, int id, out string msg, Section? dest = null)
        {
            msg = "";
            if (IsAr(st)) { msg = "full builder on PHONE"; return false; }
            if (id <= 0) { msg = "No card."; return false; }
            if (st.Inv.CountOnHand(id) <= 0) { msg = "Home only."; return false; }
            var def = st.Db?.Get(id);
            var free = st.Inv.FreeCopiesForDeck(id, st.DeckIndex);
            var inDeck = Cnt(st.Main, id) + Cnt(st.Extra, id) + Cnt(st.Side, id);
            var labOpen = LabAdminService.IsAdmin(AppSession.Ensure()?.Account);
            if (!ErazDeckRules.CanAddToDeck(def, inDeck, ErazFormat.Original, out msg, labOpen))
                return false;
            if (inDeck >= free) { msg = "No free copies."; return false; }
            var section = dest ?? RouteAdd(st, def);
            var list = ListOf(st, section);
            switch (section)
            {
                case Section.Main:
                    if (def != null && def.IsExtraDeck) { msg = "Goes in Extra."; return false; }
                    if (list.Count >= TcgRules.MainDeckMax) { msg = "Main full."; return false; }
                    st.Main.Add(id);
                    break;
                case Section.Extra:
                    if (def != null && !def.IsExtraDeck) { msg = "Extra only."; return false; }
                    if (list.Count >= TcgRules.ExtraDeckMax) { msg = "Extra full."; return false; }
                    st.Extra.Add(id);
                    break;
                case Section.Side:
                    if (list.Count >= TcgRules.SideDeckMax) { msg = "Side full."; return false; }
                    st.Side.Add(id);
                    break;
            }

            st.Section = section;
            PersistDeck(st);
            msg = "+ " + (def?.name ?? "#" + id);
            return true;
        }

        static Section RouteAdd(State st, CardDef def)
        {
            if (st.Section == Section.Side) return Section.Side;
            if (def != null && def.IsExtraDeck) return Section.Extra;
            return Section.Main;
        }

        static bool TryRem(State st, int id, out string msg, Section? from = null)
        {
            msg = "";
            if (IsAr(st)) { msg = "full builder on PHONE"; return false; }
            var list = from.HasValue ? ListOf(st, from.Value) : FindListWith(st, id);
            if (list == null) { msg = "Not in deck."; return false; }
            var i = list.FindLastIndex(x => x == id);
            if (i < 0) { msg = "Not in deck."; return false; }
            list.RemoveAt(i);
            PersistDeck(st);
            msg = "− removed";
            return true;
        }

        static List<int> ListOf(State st, Section section) => section switch
        {
            Section.Extra => st.Extra,
            Section.Side => st.Side,
            _ => st.Main
        };

        static List<int> FindListWith(State st, int id)
        {
            if (st.Section != Section.Main && Cnt(ListOf(st, st.Section), id) > 0)
                return ListOf(st, st.Section);
            if (Cnt(st.Main, id) > 0) return st.Main;
            if (Cnt(st.Extra, id) > 0) return st.Extra;
            if (Cnt(st.Side, id) > 0) return st.Side;
            return null;
        }

        static List<int> SecList(State st) => ListOf(st, st.Section);

        static int Cnt(List<int> list, int id)
        {
            var n = 0;
            foreach (var x in list)
                if (x == id) n++;
            return n;
        }

        static void LoadDeck(State st)
        {
            st.Main.Clear();
            st.Extra.Clear();
            st.Side.Clear();
            if (st.Inv == null) return;
            st.Inv.EnsureDeckBoxSlots();
            if (st.Inv.deckBoxes == null || st.Inv.deckBoxes.Length == 0) return;
            var box = st.Inv.deckBoxes[ClampDeck(st)];
            if (box?.main != null) st.Main.AddRange(box.main);
            if (box?.extra != null) st.Extra.AddRange(box.extra);
            if (box?.side != null) st.Side.AddRange(box.side);
            if (box != null) box.occupied = true;
            if (!st.RevertArmed || st.RevertDeckIndex != ClampDeck(st))
            {
                st.RevertMain = new List<int>(st.Main);
                st.RevertExtra = new List<int>(st.Extra);
                st.RevertSide = new List<int>(st.Side);
                st.RevertArmed = true;
                st.RevertDeckIndex = ClampDeck(st);
            }
        }

        static bool DeckIsComplete(State st)
        {
            var n = st.Main?.Count ?? 0;
            var ex = st.Extra?.Count ?? 0;
            var side = st.Side?.Count ?? 0;
            return n >= TcgRules.MainDeckMin && n <= TcgRules.MainDeckMax
                   && ex <= TcgRules.ExtraDeckMax
                   && side <= TcgRules.SideDeckMax;
        }

        static string IncompleteDeckLabel(State st)
        {
            var n = st.Main?.Count ?? 0;
            var need = Mathf.Max(0, TcgRules.MainDeckMin - n);
            var extra = st.Extra?.Count ?? 0;
            var side = st.Side?.Count ?? 0;
            var msg = n < TcgRules.MainDeckMin
                ? $"Main {n}/{TcgRules.MainDeckMin} — add {need} more"
                : n > TcgRules.MainDeckMax
                    ? $"Main {n}/{TcgRules.MainDeckMax} — remove extras"
                    : $"Main {n}";
            if (extra > TcgRules.ExtraDeckMax)
                msg += $" · Extra {extra}/{TcgRules.ExtraDeckMax}";
            if (side > TcgRules.SideDeckMax)
                msg += $" · Side {side}/{TcgRules.SideDeckMax}";
            return msg;
        }

        static void ApplyRevert(State st)
        {
            st.Main = new List<int>(st.RevertMain ?? new List<int>());
            st.Extra = new List<int>(st.RevertExtra ?? new List<int>());
            st.Side = new List<int>(st.RevertSide ?? new List<int>());
            PersistDeck(st);
        }

        static void RequestLeaveEditor(State st)
        {
            if (DeckIsComplete(st))
            {
                PersistDeck(st);
                st.OnClose?.Invoke();
                return;
            }

            OpenLeaveGuard(st, "REVERT & LEAVE", () =>
            {
                ApplyRevert(st);
                st.OnClose?.Invoke();
            });
        }

        static void TrySwitchDeck(State st, int nextIndex, string nextName)
        {
            CloseDeckDropdown(st);
            void Go()
            {
                st.RevertArmed = false;
                st.DeckIndex = nextIndex;
                st.Inv.SetActivePlayDeck(nextIndex);
                Save(st);
                st.Section = Section.Main;
                st.Phase = Phase.Editor;
                FreeUiKit.PlayConfirm();
                st.Rebuild();
                Status(st, "Switched to " + nextName, true);
            }

            if (DeckIsComplete(st))
            {
                PersistDeck(st);
                Go();
                return;
            }

            OpenLeaveGuard(st, "REVERT & SWITCH", () =>
            {
                ApplyRevert(st);
                Go();
            });
        }

        /// <summary>
        /// Incomplete-deck gate: stay in the editor, or revert this session's
        /// edits (the snapshot taken when the deck was opened) and continue.
        /// </summary>
        static void OpenLeaveGuard(State st, string confirmLabel, Action onRevert)
        {
            var host = st.Window != null ? st.Window : st.Body;
            if (host == null)
            {
                onRevert?.Invoke();
                return;
            }

            var old = host.Find("LeaveGuard");
            if (old != null) FloatingPanel.DestroyNow(old.gameObject);

            var dim = new GameObject("LeaveGuard", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(host, false);
            FloatingPanel.Stretch(dim.GetComponent<RectTransform>());
            var dimImg = dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = new Color(0f, 0f, 0f, 0.62f);
            dimImg.raycastTarget = true;

            var sheet = new GameObject("Sheet", typeof(RectTransform), typeof(Image));
            sheet.transform.SetParent(dim.transform, false);
            FloatingPanel.Place(sheet.GetComponent<RectTransform>(), 0.10f, 0.32f, 0.90f, 0.70f);
            var sImg = sheet.GetComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(0.07f, 0.09f, 0.13f, 0.97f);
            sImg.raycastTarget = true;

            var title = Label(sheet.transform, "DECK INCOMPLETE", 16, DuelystUi.GoldHot, TextAnchor.MiddleCenter);
            FloatingPanel.Place(title.rectTransform, 0.06f, 0.74f, 0.94f, 0.94f);

            var body = Label(sheet.transform,
                IncompleteDeckLabel(st) +
                "\nA constructed Main Deck needs 40–60 cards." +
                "\nStay and finish, or revert this session's edits.",
                13, DuelystUi.TextCream, TextAnchor.MiddleCenter);
            FloatingPanel.Place(body.rectTransform, 0.08f, 0.38f, 0.92f, 0.74f);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;

            void CloseGuard()
            {
                if (dim != null) UnityEngine.Object.Destroy(dim);
            }

            dim.GetComponent<Button>().onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                CloseGuard();
            });

            var stay = DeckChromeButton(sheet.transform, "STAY", () =>
            {
                FreeUiKit.PlayClick();
                CloseGuard();
            }, new Color(0.10f, 0.38f, 0.48f, 1f), compact: true);
            FloatingPanel.Place(stay.GetComponent<RectTransform>(), 0.06f, 0.08f, 0.46f, 0.32f);

            var revert = DeckChromeButton(sheet.transform, confirmLabel, () =>
            {
                FreeUiKit.PlayConfirm();
                CloseGuard();
                onRevert?.Invoke();
            }, new Color(0.42f, 0.12f, 0.16f, 1f), compact: true);
            FloatingPanel.Place(revert.GetComponent<RectTransform>(), 0.52f, 0.08f, 0.94f, 0.32f);
        }

        static void PersistDeck(State st)
        {
            if (st?.Inv == null) return;
            st.Inv.EnsureDeckBoxSlots();
            if (st.Inv.deckBoxes == null || st.Inv.deckBoxes.Length == 0) return;
            var i = ClampDeck(st);
            var box = st.Inv.deckBoxes[i];
            box.occupied = true;
            box.main = st.Main.ToArray();
            box.extra = st.Extra.ToArray();
            box.side = st.Side.ToArray();
            st.Inv.deckBoxes[i] = box;
            Save(st);
        }

        static int ClampDeck(State st)
        {
            var n = st?.Inv?.deckBoxes?.Length ?? 0;
            if (n <= 0) return 0;
            return Mathf.Clamp(st.DeckIndex, 0, n - 1);
        }

        static void Save(State st)
        {
            if (st.Acc != null) ProgressionService.Persist(st.Acc);
        }

        static void Status(State st, string msg, bool ok)
        {
            if (st.Status == null) return;
            st.Status.text = msg ?? "";
            st.Status.color = ok
                ? new Color(0.55f, 0.95f, 0.8f, 0.95f)
                : new Color(1f, 0.5f, 0.48f, 0.95f);
        }

        // ── Compact widgets ──────────────────────────────────────────────────

        static Button Btn(Transform parent, string label, Action onClick, bool gold = false,
            bool compact = false)
        {
            var fill = gold
                ? new Color(0.12f, 0.42f, 0.52f, 0.50f)
                : new Color(0.10f, 0.32f, 0.44f, 0.45f);
            if (!gold && compact)
                fill = new Color(0.10f, 0.13f, 0.18f, 0.40f);
            return DeckChromeButton(parent, label, onClick, fill, compact);
        }

        static void RowHeight(Button b, float h)
        {
            var le = b.GetComponent<LayoutElement>();
            le.minHeight = h;
            le.preferredHeight = h;
        }

        static void Chip(Transform host, string label, bool on, Action act)
        {
            var b = DeckChromeButton(host, label, () =>
            {
                FreeUiKit.PlayClick();
                act();
            }, on
                ? new Color(0.10f, 0.38f, 0.50f, 1f)
                : new Color(0.10f, 0.13f, 0.18f, 1f), compact: true);
            var le = b.GetComponent<LayoutElement>();
            le.minWidth = 56;
            le.preferredWidth = 70;
            le.minHeight = 32;
            le.preferredHeight = 32;
        }

        static Transform VList(Transform parent, float x0, float y0, float x1, float y1)
        {
            var panel = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            panel.transform.SetParent(parent, false);
            FloatingPanel.Place(panel.GetComponent<RectTransform>(), x0, y0, x1, y1);
            panel.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            // List backdrop: enough opacity that rows don't vanish on AR / map
            panel.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.55f);
            panel.GetComponent<Image>().raycastTarget = true;

            var vp = new GameObject("VP", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            vp.transform.SetParent(panel.transform, false);
            FloatingPanel.Stretch(vp.GetComponent<RectTransform>(), 4f);
            vp.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            vp.GetComponent<Image>().color = new Color(1, 1, 1, 0.02f);
            vp.GetComponent<Image>().raycastTarget = true;

            var content = new GameObject("C", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(vp.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.spacing = 5;
            v.padding = new RectOffset(4, 4, 4, 4);
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sc = panel.GetComponent<ScrollRect>();
            sc.viewport = vp.GetComponent<RectTransform>();
            sc.content = crt;
            sc.horizontal = false;
            sc.vertical = true;
            sc.movementType = ScrollRect.MovementType.Clamped;
            sc.scrollSensitivity = GridScrollSensitivity;
            sc.inertia = true;
            sc.decelerationRate = 0.22f;
            return content.transform;
        }

        static Transform HList(Transform parent, float x0, float y0, float x1, float y1)
        {
            var panel = new GameObject("HList", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            panel.transform.SetParent(parent, false);
            FloatingPanel.Place(panel.GetComponent<RectTransform>(), x0, y0, x1, y1);
            panel.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            panel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, MenuChromePrefs.RowAlpha * 0.4f);

            var vp = new GameObject("VP", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            vp.transform.SetParent(panel.transform, false);
            FloatingPanel.Stretch(vp.GetComponent<RectTransform>(), 2f);
            vp.GetComponent<Image>().sprite = UiFoundation.WhiteSprite();
            vp.GetComponent<Image>().color = new Color(1, 1, 1, 0.02f);

            var content = new GameObject("C", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(vp.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 0);
            crt.anchorMax = new Vector2(0, 1);
            crt.pivot = new Vector2(0, 0.5f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var h = content.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 3;
            h.padding = new RectOffset(3, 3, 2, 2);
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            content.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sc = panel.GetComponent<ScrollRect>();
            sc.viewport = vp.GetComponent<RectTransform>();
            sc.content = crt;
            sc.horizontal = true;
            sc.vertical = false;
            sc.scrollSensitivity = GridScrollSensitivity;
            sc.inertia = true;
            sc.decelerationRate = 0.22f;
            return content.transform;
        }

        /// <summary>
        /// CARD LIST well. Returns the scroll content (chip host). The panel
        /// itself is scroll's transform — pin that, never the content, or
        /// phase-normalized anchors squeeze the chips into the right edge.
        /// </summary>
        static Transform ChipGrid(Transform parent, State st, float x0, float y0, float x1, float y1,
            string name, out ScrollRect scroll)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect),
                typeof(Button));
            panel.transform.SetParent(parent, false);
            FloatingPanel.Place(panel.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var pImg = panel.GetComponent<Image>();
            var listPlate = ImagineAssets.PanelHolo() ?? ImagineAssets.PanelMenuGlass();
            if (listPlate != null)
            {
                pImg.sprite = listPlate;
                pImg.type = listPlate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                pImg.color = Color.white;
            }
            else
            {
                pImg.sprite = UiFoundation.WhiteSprite();
                pImg.color = new Color(0.02f, 0.03f, 0.06f, 0.12f);
            }
            pImg.raycastTarget = true;
            var empty = panel.GetComponent<Button>();
            empty.targetGraphic = pImg;
            empty.transition = Selectable.Transition.None;
            empty.onClick.AddListener(() => HideInspect(st));

            var vp = new GameObject("VP", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            vp.transform.SetParent(panel.transform, false);
            FloatingPanel.Stretch(vp.GetComponent<RectTransform>(), 4f);
            var vpImg = vp.GetComponent<Image>();
            vpImg.sprite = UiFoundation.WhiteSprite();
            vpImg.color = new Color(1, 1, 1, 0.01f);
            vpImg.raycastTarget = true;
            vp.GetComponent<RectMask2D>().padding = Vector4.zero;

            var content = new GameObject("C", typeof(RectTransform));
            content.transform.SetParent(vp.transform, false);
            var crt = content.GetComponent<RectTransform>();
            // Top-left origin so chips tile from the panel's left edge. Pivot 0.5
            // plus GridLayoutGroup was packing the collection into a thin strip
            // on the far right of the holo well.
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            scroll = panel.GetComponent<ScrollRect>();
            scroll.viewport = vp.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = GridScrollSensitivity;
            scroll.inertia = true;
            scroll.decelerationRate = 0.18f;

            var fit = content.AddComponent<DeckPoolFit>();
            fit.Viewport = vp.GetComponent<RectTransform>();
            if (name == "PoolGrid")
            {
                scroll.onValueChanged.AddListener(_ => TryAppendPool(st));
            }

            return content.transform;
        }

        static void RefitPool(State st)
        {
            if (st?.PoolGrid is RectTransform rt)
                rt.GetComponent<DeckPoolFit>()?.Fit(force: true);
        }

        /// <summary>
        /// Tiles collection chips left→right, top→bottom, filling the viewport
        /// width. Replaces GridLayoutGroup + ContentSizeFitter, which did not
        /// reliably fill the wide-layout CARD LIST well.
        /// </summary>
        sealed class DeckPoolFit : MonoBehaviour
        {
            public RectTransform Viewport;
            bool _fitting;
            int _lastN = -1;
            float _lastW = -1f;
            float _lastH = -1f;

            void OnEnable() => Fit();
            void Start() => Fit();
            void OnRectTransformDimensionsChange() => Fit();
            void LateUpdate() => Fit();

            public void Fit(bool force = false)
            {
                if (_fitting) return;
                var rt = transform as RectTransform;
                if (rt == null) return;
                var vp = Viewport != null ? Viewport : rt.parent as RectTransform;
                if (vp == null) return;
                var w = vp.rect.width;
                var h = vp.rect.height;
                if (w < 16f || h < 8f) return;
                var n = rt.childCount;
                if (!force && n == _lastN && Mathf.Abs(w - _lastW) < 0.5f && Mathf.Abs(h - _lastH) < 0.5f)
                    return;

                _fitting = true;
                _lastN = n;
                _lastW = w;
                _lastH = h;
                const float pad = 6f;
                const float gap = ChipGap;
                const float target = 96f;
                var cols = Mathf.Clamp(
                    Mathf.FloorToInt((w - pad * 2f + gap) / (target + gap)), 3, 12);
                var cardW = (w - pad * 2f - gap * (cols - 1)) / cols;
                var cardH = cardW * (ChipH / ChipW);
                var rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, n) / (float)cols));
                var totalH = pad * 2f + rows * cardH + Mathf.Max(0, rows - 1) * gap;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(h, totalH));
                for (var i = 0; i < n; i++)
                {
                    var child = rt.GetChild(i) as RectTransform;
                    if (child == null) continue;
                    var col = i % cols;
                    var row = i / cols;
                    child.anchorMin = new Vector2(0f, 1f);
                    child.anchorMax = new Vector2(0f, 1f);
                    child.pivot = new Vector2(0f, 1f);
                    child.sizeDelta = new Vector2(cardW, cardH);
                    child.anchoredPosition = new Vector2(
                        pad + col * (cardW + gap), -(pad + row * (cardH + gap)));
                }

                _fitting = false;
            }
        }

        /// <summary>
        /// Spread card-face loads across frames so opening the editor cannot
        /// decode hundreds of textures on one hitch.
        /// </summary>
        sealed class DeferredCardArt : MonoBehaviour
        {
            static int _frame = -1;
            static int _loadedThisFrame;

            CardDatabase _db;
            int _id;
            Image _img;

            public void Bind(CardDatabase db, int id, Image img)
            {
                _db = db;
                _id = id;
                _img = img;
            }

            void LateUpdate()
            {
                if (_img == null)
                {
                    enabled = false;
                    return;
                }

                if (_frame != Time.frameCount)
                {
                    _frame = Time.frameCount;
                    _loadedThisFrame = 0;
                }

                if (_loadedThisFrame >= 4) return;
                _loadedThisFrame++;
                var spr = _db != null ? _db.GetArt(_id) : null;
                if (spr != null)
                    _img.sprite = spr;
                enabled = false;
            }
        }

        sealed class DeckChipInteract : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
            IInitializePotentialDragHandler
        {
            State _st;
            int _id;
            bool _fromPool;
            Section _section;
            ScrollRect _scroll;
            bool _passScroll;
            bool _cardDrag;

            public void Bind(State st, int id, bool fromPool, Section section, ScrollRect scroll)
            {
                _st = st;
                _id = id;
                _fromPool = fromPool;
                _section = section;
                _scroll = scroll;
            }

            public void OnInitializePotentialDrag(PointerEventData e)
            {
                _passScroll = false;
                _cardDrag = false;
                if (_scroll != null)
                    _scroll.OnInitializePotentialDrag(e);
            }

            public void OnBeginDrag(PointerEventData e)
            {
                var vert = Mathf.Abs(e.delta.y) >= Mathf.Abs(e.delta.x) * 0.85f;
                _passScroll = vert && _scroll != null;
                _cardDrag = !_passScroll;
                if (_passScroll)
                    _scroll.OnBeginDrag(e);
                else
                    BeginCardDrag(e);
            }

            public void OnDrag(PointerEventData e)
            {
                if (_passScroll && _scroll != null)
                    _scroll.OnDrag(e);
                else if (_cardDrag)
                    MoveGhost(e);
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (_passScroll && _scroll != null)
                    _scroll.OnEndDrag(e);
                else if (_cardDrag)
                    EndCardDrag(e);
                _passScroll = false;
                _cardDrag = false;
            }

            void BeginCardDrag(PointerEventData ped)
            {
                if (_st == null) return;
                _st.DragId = _id;
                _st.DragFromPool = _fromPool;
                _st.DragSection = _section;
                _st.SelectedId = _id;
                if (_st.Ghost != null) UnityEngine.Object.Destroy(_st.Ghost);
                var g = new GameObject("G", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                g.transform.SetParent(_st.DragLayer, false);
                var rt = g.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(ChipW * 1.15f, ChipH * 1.15f);
                var img = g.GetComponent<Image>();
                img.sprite = _st.Db != null ? _st.Db.GetArt(_id) : null;
                if (img.sprite == null) img.sprite = YgoCardFrames.CardBack() ?? UiFoundation.WhiteSprite();
                img.preserveAspect = true;
                img.raycastTarget = false;
                g.GetComponent<CanvasGroup>().blocksRaycasts = false;
                g.GetComponent<CanvasGroup>().alpha = 0.88f;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _st.DragLayer as RectTransform, ped.position, ped.pressEventCamera, out var loc);
                rt.anchoredPosition = loc;
                _st.Ghost = g;
            }

            void MoveGhost(PointerEventData ped)
            {
                if (_st?.Ghost == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _st.DragLayer as RectTransform, ped.position, ped.pressEventCamera, out var loc);
                _st.Ghost.GetComponent<RectTransform>().anchoredPosition = loc;
            }

            void EndCardDrag(PointerEventData ped)
            {
                if (_st == null) return;
                if (_st.Ghost != null)
                {
                    UnityEngine.Object.Destroy(_st.Ghost);
                    _st.Ghost = null;
                }

                var cam = ped.pressEventCamera;
                var onto = HitPile(_st, ped.position, cam);
                var ontoPool = ContainsScreen(_st.PoolScroll != null
                    ? _st.PoolScroll.transform as RectTransform
                    : null, ped.position, cam);

                if (_st.DragFromPool && onto.HasValue)
                {
                    if (TryAdd(_st, _st.DragId, out var msg, onto))
                    {
                        FreeUiKit.PlayConfirm();
                        Status(_st, msg, true);
                        if (_st.RefreshCards != null) _st.RefreshCards();
                        else _st.Rebuild();
                    }
                    else Status(_st, msg, false);
                }
                else if (!_st.DragFromPool && ontoPool)
                {
                    if (TryRem(_st, _st.DragId, out var msg, _st.DragSection))
                    {
                        FreeUiKit.PlayClick();
                        Status(_st, msg, true);
                        if (_st.RefreshCards != null) _st.RefreshCards();
                        else _st.Rebuild();
                    }
                }
                else if (!_st.DragFromPool && onto.HasValue && onto.Value != _st.DragSection)
                {
                    if (TryRem(_st, _st.DragId, out _, _st.DragSection)
                        && TryAdd(_st, _st.DragId, out var msg, onto))
                    {
                        FreeUiKit.PlayConfirm();
                        Status(_st, msg, true);
                        if (_st.RefreshCards != null) _st.RefreshCards();
                        else _st.Rebuild();
                    }
                    else if (_st.RefreshCards != null) _st.RefreshCards();
                }

                _st.DragId = 0;
            }
        }

        static Section? HitPile(State st, Vector2 screen, Camera cam)
        {
            if (ContainsScreen(st.MainTray, screen, cam)) return Section.Main;
            if (ContainsScreen(st.ExtraTray, screen, cam)) return Section.Extra;
            if (ContainsScreen(st.SideTray, screen, cam)) return Section.Side;
            return null;
        }

        static bool ContainsScreen(RectTransform rt, Vector2 screen, Camera cam)
        {
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam);
        }

        static Text L(Transform parent, string text, int size, Color color, TextAnchor align) =>
            Label(parent, text, size, color, align);

        /// <summary>
        /// Deck editor always uses a large sheet so 4-wide named card grids breathe.
        /// Size pref only trims a few percent from the edges.
        /// </summary>
        static void GetDeckWindowAnchors(UiPresentation presentation,
            out float x0, out float y0, out float x1, out float y1)
        {
            // Large default footprint
            x0 = 0.01f;
            y0 = 0.015f;
            x1 = 0.99f;
            y1 = 0.985f;

            // Optional trim from player size pref (still larger than other menus)
            switch (MenuChromePrefs.Size)
            {
                case MenuChromePrefs.SizeLevel.Compact:
                    x0 = 0.04f; y0 = 0.05f; x1 = 0.96f; y1 = 0.95f;
                    break;
                case MenuChromePrefs.SizeLevel.Medium:
                    x0 = 0.03f; y0 = 0.04f; x1 = 0.97f; y1 = 0.96f;
                    break;
            }

            if (presentation == UiPresentation.ArDiskHolo ||
                presentation == UiPresentation.ArWorldPanel)
            {
                // Slight AR inset so companion chrome still shows
                x0 += 0.015f;
                x1 -= 0.015f;
                y0 += 0.02f;
                y1 -= 0.02f;
            }
        }
    }
}
