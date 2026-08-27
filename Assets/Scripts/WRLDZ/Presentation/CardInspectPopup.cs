using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.UI;
using WRLDZ.UI.Shell;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Full legal TCG card popup — only when the player selects a card.
    /// Shows art, frame info, stats, and complete effect text, plus legal actions.
    /// Local-only (never reveals hidden info to the opponent).
    /// </summary>
    public class CardInspectPopup : MonoBehaviour
    {
        GameObject _root;
        Image _art;
        Image _cardFrame;
        Text _title;
        Text _typeLine;
        Text _stats;
        Text _desc;
        Transform _actionRow;
        CardDatabase _db;
        Action _onClose;
        Coroutine _hideCo;
        CardInstance _shownCard;
        int _showGen;

        /// <summary>Card currently shown (null if closed).</summary>
        public CardInstance ShownCard => IsOpen ? _shownCard : null;

        public static CardInspectPopup Create(Transform canvasRoot)
        {
            var host = new GameObject("CardInspectPopup", typeof(RectTransform));
            host.transform.SetParent(canvasRoot, false);
            Stretch(host.GetComponent<RectTransform>());
            var pop = host.AddComponent<CardInspectPopup>();
            pop.Build(host.transform);
            host.SetActive(false);
            return pop;
        }

        void Build(Transform root)
        {
            _root = root.gameObject;

            // Dimmer
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(root, false);
            Stretch(dim.GetComponent<RectTransform>());
            var dimImg = dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = new Color(0f, 0f, 0f, 0.18f);
            dimImg.raycastTarget = true;
            dim.GetComponent<Button>().onClick.AddListener(Hide);

            // Card panel — dark sheet with framed legal card (CYDB templates)
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.16f, 0.28f);
            prt.anchorMax = new Vector2(0.84f, 0.78f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            var pimg = panel.GetComponent<Image>();
            pimg.sprite = UiFoundation.WhiteSprite();
            pimg.color = new Color(0.04f, 0.06f, 0.10f, 0.55f);
            pimg.raycastTarget = true;
            var glass = ImagineAssets.HudCalloutGlass() ?? ImagineAssets.HudIslandGlass();
            if (glass != null)
            {
                var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
                frame.transform.SetParent(panel.transform, false);
                Stretch(frame.GetComponent<RectTransform>());
                var fi = frame.GetComponent<Image>();
                fi.sprite = glass;
                fi.color = Color.white;
                fi.raycastTarget = false;
            }

            // Framed card preview (left) — template + art hole
            var cardPreview = new GameObject("CardPreview", typeof(RectTransform), typeof(Image));
            cardPreview.transform.SetParent(panel.transform, false);
            var cprt = cardPreview.GetComponent<RectTransform>();
            cprt.anchorMin = new Vector2(0.04f, 0.22f);
            cprt.anchorMax = new Vector2(0.40f, 0.94f);
            cprt.offsetMin = Vector2.zero;
            cprt.offsetMax = Vector2.zero;
            _cardFrame = cardPreview.GetComponent<Image>();
            _cardFrame.sprite = UiFoundation.WhiteSprite();
            _cardFrame.preserveAspect = true;
            _cardFrame.color = Color.white;
            _cardFrame.raycastTarget = false;

            var artGo = new GameObject("Art", typeof(RectTransform), typeof(Image));
            artGo.transform.SetParent(cardPreview.transform, false);
            var artRt = artGo.GetComponent<RectTransform>();
            artRt.anchorMin = YgoCardFrames.ArtAnchorMin;
            artRt.anchorMax = YgoCardFrames.ArtAnchorMax;
            artRt.offsetMin = Vector2.zero;
            artRt.offsetMax = Vector2.zero;
            _art = artGo.GetComponent<Image>();
            _art.sprite = UiFoundation.WhiteSprite();
            _art.preserveAspect = false;
            _art.color = Color.white;
            _art.raycastTarget = false;

            // Title / type / stats (right of card)
            _title = MakeText(panel.transform, "Title", 18, true,
                0.42f, 0.84f, 0.90f, 0.96f, WrldzTheme.GoldHot, TextAnchor.MiddleLeft);

            _typeLine = MakeText(panel.transform, "Type", 13, false,
                0.42f, 0.76f, 0.96f, 0.84f, WrldzTheme.Cyan, TextAnchor.MiddleLeft);

            _stats = MakeText(panel.transform, "Stats", 14, true,
                0.42f, 0.68f, 0.96f, 0.76f, Color.white, TextAnchor.MiddleLeft);

            // Full legal text (scroll area) — right + bottom
            var scrollGo = new GameObject("DescScroll", typeof(RectTransform), typeof(Image), typeof(Mask));
            scrollGo.transform.SetParent(panel.transform, false);
            var srt = scrollGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.42f, 0.20f);
            srt.anchorMax = new Vector2(0.96f, 0.66f);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var simg = scrollGo.GetComponent<Image>();
            simg.sprite = UiFoundation.WhiteSprite();
            simg.color = new Color(0.02f, 0.03f, 0.06f, 0.35f);
            scrollGo.GetComponent<Mask>().showMaskGraphic = true;

            var descGo = new GameObject("Desc", typeof(RectTransform), typeof(Text));
            descGo.transform.SetParent(scrollGo.transform, false);
            Stretch(descGo.GetComponent<RectTransform>(), 8, 6);
            _desc = descGo.GetComponent<Text>();
            WrldzType.Style(_desc, 15, display: false);
            _desc.color = new Color(0.92f, 0.93f, 0.96f, 1f);
            _desc.alignment = TextAnchor.UpperLeft;
            _desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            _desc.verticalOverflow = VerticalWrapMode.Overflow;
            _desc.raycastTarget = false;

            // Actions row
            var acts = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            acts.transform.SetParent(panel.transform, false);
            var art2 = acts.GetComponent<RectTransform>();
            art2.anchorMin = new Vector2(0.04f, 0.03f);
            art2.anchorMax = new Vector2(0.96f, 0.18f);
            art2.offsetMin = Vector2.zero;
            art2.offsetMax = Vector2.zero;
            var h = acts.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 2, 2);
            _actionRow = acts.transform;

            // Close chip
            var close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(panel.transform, false);
            var crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.88f, 0.92f);
            crt.anchorMax = new Vector2(0.98f, 0.99f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var cimg = close.GetComponent<Image>();
            cimg.sprite = DuelystUi.BtnClose() ?? UiFoundation.WhiteSprite();
            cimg.color = Color.white;
            cimg.preserveAspect = true;
            var cbtn = close.GetComponent<Button>();
            cbtn.targetGraphic = cimg;
            cbtn.onClick.AddListener(Hide);
            var ct = MakeText(close.transform, "X", 18, true, 0, 0, 1, 1, Color.white, TextAnchor.MiddleCenter);
            ct.text = "✕";
        }

        public void Show(CardInstance card, CardDatabase db, bool showFace, List<(string label, Color color, Action act)> actions,
            Action onClose = null, string closeLabel = "CLOSE")
        {
            if (card == null) return;
            _showGen++;
            _db = db;
            _onClose = onClose;
            _shownCard = card;
            if (_hideCo != null)
            {
                StopCoroutine(_hideCo);
                _hideCo = null;
            }
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();

            var def = card.Def ?? db?.Get(card.CardId);
            var face = showFace;

            // CardArt/{id}.jpg = full TCG face. Prefer that as the whole card image.
            // Backs use StreamingSprite / YgoFrames card-back packs.
            if (_cardFrame != null)
            {
                if (face)
                {
                    var fullFace = db?.GetArt(card.CardId);
                    _cardFrame.sprite = fullFace
                                        ?? YgoCardFrames.FrameFor(def)
                                        ?? UiFoundation.WhiteSprite();
                    _cardFrame.color = Color.white;
                    _cardFrame.preserveAspect = true;
                }
                else
                {
                    _cardFrame.sprite = StreamingSprite.CardBack()
                                        ?? YgoCardFrames.CardBack()
                                        ?? UiFoundation.WhiteSprite();
                    _cardFrame.color = Color.white;
                }
            }

            // Secondary art layer only needed for blank-template + crop path (no full face).
            if (_art != null)
            {
                var fullFace = face ? db?.GetArt(card.CardId) : null;
                if (face && fullFace == null)
                {
                    _art.enabled = true;
                    _art.sprite = UiFoundation.WhiteSprite();
                    _art.color = new Color(0.2f, 0.25f, 0.35f, 1f);
                    var artRt = _art.rectTransform;
                    artRt.anchorMin = YgoCardFrames.ArtAnchorMin;
                    artRt.anchorMax = YgoCardFrames.ArtAnchorMax;
                }
                else
                {
                    // Full face already on _cardFrame, or face-down back — hide nested art
                    _art.enabled = false;
                }
            }

            if (_title != null)
                _title.text = face ? (card.Name ?? def?.name ?? "Card") : "Set Card";

            if (_typeLine != null)
            {
                if (!face)
                    _typeLine.text = "Face-down · information hidden";
                else if (def != null)
                    _typeLine.text = BuildTypeLine(def);
                else
                    _typeLine.text = card.Def?.type ?? "";
            }

            if (_stats != null)
            {
                if (!face)
                    _stats.text = "—";
                else if (def != null && def.IsMonster)
                    _stats.text =
                        $"ATK {card.CurrentAtk}  /  DEF {card.CurrentDef}   ·   Level {card.Level}   ·   {def.attribute} / {def.race}";
                else if (def != null)
                    _stats.text = def.type ?? "Spell/Trap";
                else
                    _stats.text = "";
            }

            if (_desc != null)
            {
                if (!face)
                    _desc.text = "This card is set. Its identity and text are hidden until it is flipped or activated.";
                else
                    _desc.text = BuildFullText(def, card);
            }

            // Actions — DestroyImmediate so a same-frame re-Show cannot stack duplicates
            for (var i = _actionRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(_actionRow.GetChild(i).gameObject);

            if (actions != null)
            {
                foreach (var a in actions)
                {
                    var label = a.label;
                    var col = a.color;
                    var act = a.act;
                    var btnGo = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button),
                        typeof(LayoutElement));
                    btnGo.transform.SetParent(_actionRow, false);
                    var le = btnGo.GetComponent<LayoutElement>();
                    le.preferredHeight = 58;
                    le.minHeight = 52;
                    le.minWidth = 72;
                    le.preferredWidth = Mathf.Clamp(18 + label.Length * 11, 88, 160);
                    le.flexibleWidth = 1;
                    var img = btnGo.GetComponent<Image>();
                    var chip = col.r > 0.45f && col.g < 0.35f
                        ? ImagineAssets.HudChipDanger()
                        : col.r > 0.5f && col.g > 0.4f
                            ? ImagineAssets.HudChipGold()
                            : ImagineAssets.HudChipCyan();
                    img.sprite = chip ?? UiFoundation.WhiteSprite();
                    img.type = Image.Type.Simple;
                    img.color = chip != null ? Color.white : new Color(col.r, col.g, col.b, 0.38f);
                    var btn = btnGo.GetComponent<Button>();
                    btn.targetGraphic = img;
                    btn.onClick.AddListener(() =>
                    {
                        FreeUiKit.PlayClick();
                        var gen = _showGen;
                        act?.Invoke();
                        // Close after the play, but keep the overlay up this frame
                        // so the same click cannot fall through onto the hand/field.
                        // If Invoke opened a new inspect (target menu), do not hide it.
                        HideAfterPointer(gen);
                    });
                    var t = MakeText(btnGo.transform, "L", 16, true, 0, 0, 1, 1, Color.white,
                        TextAnchor.MiddleCenter);
                    t.text = label;
                    WrldzType.StyleButtonLabel(t, 16, display: true);
                    t.resizeTextForBestFit = true;
                    t.resizeTextMinSize = 14;
                    t.resizeTextMaxSize = 24;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }

            // Always Close
            var closeGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            closeGo.transform.SetParent(_actionRow, false);
            var closeLe = closeGo.GetComponent<LayoutElement>();
            closeLe.preferredHeight = 58;
            closeLe.minWidth = 72;
            closeLe.preferredWidth = 96;
            closeLe.flexibleWidth = 0;
            var cImg = closeGo.GetComponent<Image>();
            cImg.sprite = DuelystUi.BtnSecondary() ?? UiFoundation.WhiteSprite();
            cImg.type = cImg.sprite != null && cImg.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            cImg.color = Color.white;
            closeGo.GetComponent<Button>().onClick.AddListener(Hide);
            var ct = MakeText(closeGo.transform, "L", 16, true, 0, 0, 1, 1, Color.white, TextAnchor.MiddleCenter);
            ct.text = string.IsNullOrEmpty(closeLabel) ? "CLOSE" : closeLabel;
            WrldzType.StyleButtonLabel(ct, 16, display: true);
            ct.resizeTextForBestFit = true;
            ct.resizeTextMinSize = 14;
            ct.resizeTextMaxSize = 22;
        }

        public void Hide()
        {
            Hide(invokeClose: true);
        }

        /// <summary>Close without firing onClose (switching to another overlay).</summary>
        public void HideQuiet() => Hide(invokeClose: false);

        void Hide(bool invokeClose)
        {
            if (_hideCo != null)
            {
                StopCoroutine(_hideCo);
                _hideCo = null;
            }

            if (_root != null) _root.SetActive(false);
            _shownCard = null;
            var close = _onClose;
            _onClose = null;
            if (invokeClose)
                close?.Invoke();
        }

        /// <summary>
        /// Close after a play/action without letting this pointer click land on
        /// the hand or field underneath (which would reopen the viewer).
        /// Skips hide when <see cref="Show"/> ran during the click (target menu).
        /// </summary>
        public void HideAfterPointer(int gen = -1)
        {
            if (gen >= 0 && _showGen != gen)
                return;

            if (_root == null || !_root.activeSelf)
            {
                if (gen < 0 || _showGen == gen)
                    Hide();
                return;
            }

            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                if (gen < 0 || _showGen == gen)
                    Hide();
                return;
            }

            if (_hideCo != null) StopCoroutine(_hideCo);
            var captured = gen >= 0 ? gen : _showGen;
            _hideCo = StartCoroutine(HideEndOfFrame(captured));
        }

        System.Collections.IEnumerator HideEndOfFrame(int gen)
        {
            // Two frames: EventSystem finishes the click, then any same-frame
            // Refresh/rebuild cannot reopen before we go away.
            yield return null;
            yield return null;
            _hideCo = null;
            if (_showGen != gen) yield break;
            Hide();
        }

        public bool IsOpen => _root != null && _root.activeSelf;

        static string BuildTypeLine(CardDef def)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(def.type)) sb.Append(def.type);
            if (!string.IsNullOrEmpty(def.frameType)) sb.Append("  ·  ").Append(def.frameType);
            if (!string.IsNullOrEmpty(def.archetype)) sb.Append("  ·  ").Append(def.archetype);
            return sb.ToString();
        }

        static string BuildFullText(CardDef def, CardInstance card)
        {
            if (def == null)
                return card?.Name ?? "No text available.";

            var sb = new StringBuilder();
            // Official-style card text block
            if (def.IsMonster)
            {
                sb.AppendLine($"[{def.race}/{(def.type.Contains("Effect") ? "Effect" : "Normal")}]");
                if (def.level > 0) sb.AppendLine($"Level {def.level}");
            }
            else
            {
                sb.AppendLine($"[{def.type}]");
            }

            sb.AppendLine();
            var text = string.IsNullOrWhiteSpace(def.desc) ? "(No card text in database.)" : def.desc.Trim();
            sb.Append(text);
            return sb.ToString();
        }

        static Text MakeText(Transform parent, string name, int size, bool title,
            float x0, float y0, float x1, float y1, Color c, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: title);
            t.color = c;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        static void Stretch(RectTransform r, float padX = 0, float padY = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(padX, padY);
            r.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
