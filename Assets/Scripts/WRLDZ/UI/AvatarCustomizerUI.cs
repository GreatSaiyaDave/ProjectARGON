using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Avatar / profile menus (Pokémon GO profile + DMO-style character create energy):
    /// · Profile sheet (stats + open customize)
    /// · Full customizer (body, hair, eyes, outfit, accessory, colors, title, display name)
    /// Saves to local account DB and refreshes map/hub portraits.
    /// </summary>
    public partial class AvatarCustomizerUI : MonoBehaviour
    {
        Transform _canvasRoot;
        GameObject _profileSheet;
        GameObject _customizerSheet;
        AvatarPortraitView _preview;
        AvatarPortraitView _profilePortrait;
        AvatarAppearance _draft;
        Text _status;
        Text _profileName, _profileTitle, _profileHandle, _profileLevel, _profileXp;
        Text _profileWallet;
        Image _profileTeamIcon;
        Text _statDuels, _statCards, _statPath, _statSe;
        Image _profileXpFill;
        SoulBadgeView _soulBadge;
        Text _optionLabel;
        RectTransform _companionHost;
        InputField _nameField;
        Action _onSaved;
        ClothingWardrobe _wardrobe;
        Transform _optionHost;
        readonly List<Image> _slotImgs = new();
        readonly List<Outline> _slotRings = new();
        readonly List<Part> _slotParts = new();

        // Cycle indices shown as current option labels
        enum Part { Look, Hat, Facial, Shirt, Hands, Bottoms, Shoes, Accent, Title, Skin, HairColor, OutfitTint, Disk, Aura, Body, Hair, Paint, Outfit, Neck, FaceShape, Accessories, Tattoos }
        Part _focus = Part.Body;

        // 3D customization (Customization v1): live 3D preview, body / hair / colour editors
        AvatarPreview3d _preview3d;
        RectTransform _chipsHost, _stripRt, _controlsHost, _previewRt, _nameRt, _labelRt, _statusRt, _saveRt, _backRt;
        string _hairZone = "root";
        string _paintKey = "Shirt.0";
        bool _wide;

        public static AvatarCustomizerUI Ensure(Transform parent = null)
        {
            var existing = UnityEngine.Object.FindAnyObjectByType<AvatarCustomizerUI>();
            if (existing != null) return existing;
            var go = new GameObject("AvatarCustomizerUI");
            if (parent != null) go.transform.SetParent(parent, false);
            DontDestroyOnLoad(go); // survives scene hops so save callback stays valid
            return go.AddComponent<AvatarCustomizerUI>();
        }

        void Awake()
        {
            // Dedicated overlay canvas
            var canvasGo = new GameObject("AvatarCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            UiTheme.ApplyPortraitPhone(canvasGo.GetComponent<CanvasScaler>());
            _canvasRoot = canvasGo.transform;
            BuildProfileSheet();
            BuildCustomizerSheet();
            HideAllNow();
        }

        public void OpenProfile(Action onSaved = null)
        {
            StopAllCoroutines();
            _onSaved = onSaved;
            LoadDraftFromSession();
            RefreshProfile();
            HideAllNow();
            if (_profileSheet != null)
                MenuMotion.Play(this, MenuMotion.SheetIn(_profileSheet));
        }

        public void OpenCustomizer(Action onSaved = null)
        {
            StopAllCoroutines();
            _onSaved = onSaved;
            StreamingSprite.ClearCache("WRLDZ/Avatar/clothes/");
            LoadDraftFromSession();
            HideAllNow();
            if (_customizerSheet != null)
                MenuMotion.Play(this, MenuMotion.SheetIn(_customizerSheet));
            ApplyPreview();
            UpdateOptionLabel();
            RebuildOptionStrip();
            if (_nameField != null)
            {
                var acc = AppSession.Ensure().Account;
                _nameField.text = acc?.displayName ?? "";
            }
        }

        public void HideAll()
        {
            if (_profileSheet != null && _profileSheet.activeSelf)
                MenuMotion.Play(this, MenuMotion.SheetOut(_profileSheet));
            else if (_profileSheet != null)
                _profileSheet.SetActive(false);

            if (_customizerSheet != null && _customizerSheet.activeSelf)
                MenuMotion.Play(this, MenuMotion.SheetOut(_customizerSheet));
            else if (_customizerSheet != null)
                _customizerSheet.SetActive(false);
        }

        void HideAllNow()
        {
            if (_profileSheet != null) _profileSheet.SetActive(false);
            if (_customizerSheet != null) _customizerSheet.SetActive(false);
        }

        void LoadDraftFromSession()
        {
            AppSession.Ensure().RefreshFromStore();
            var acc = AppSession.Ensure().Account;
            _draft = AvatarAppearance.Default();
            if (acc != null)
            {
                // Prefer structured appearance; migrate from avatarColor if empty
                if (acc.avatar != null)
                    _draft = acc.avatar.Clone();
                else
                    _draft.accentHex = string.IsNullOrEmpty(acc.avatarColor) ? "#3ECFFF" : acc.avatarColor;
            }

            _draft.ClampToCatalog();
            if (acc != null)
            {
                acc.EnsureInventory();
                ClothingService.EnsureWardrobe(acc);
                _wardrobe = acc.inventory.wardrobe;
            }
            else
            {
                _wardrobe = new ClothingWardrobe();
                ClothingCatalog.GrantStarter(_wardrobe);
            }
        }

        void BuildProfileSheet()
        {
            _profileSheet = new GameObject("ProfileSheet", typeof(RectTransform));
            _profileSheet.transform.SetParent(_canvasRoot, false);
            GoTheme.Stretch(_profileSheet.GetComponent<RectTransform>());

            var frame = DualMenuPresenter.BuildFrame(
                _profileSheet.transform, "PROFILE", "Spirit Dueler", HideAll);
            _profileName = frame.Title;
            _profileHandle = frame.Subtitle;
            if (_profileName != null)
            {
                _profileName.resizeTextForBestFit = false;
                _profileName.horizontalOverflow = HorizontalWrapMode.Overflow;
                _profileName.verticalOverflow = VerticalWrapMode.Overflow;
            }

            if (_profileHandle != null)
            {
                WrldzType.StyleButtonLabel(_profileHandle, 14);
                _profileHandle.color = new Color(0.90f, 0.94f, 1f, 0.96f);
                _profileHandle.alignment = TextAnchor.MiddleLeft;
                _profileHandle.resizeTextForBestFit = false;
                _profileHandle.horizontalOverflow = HorizontalWrapMode.Overflow;
                _profileHandle.verticalOverflow = VerticalWrapMode.Overflow;
            }

            var body = frame.BodyHost;

            var host = new GameObject("PortraitHost", typeof(RectTransform), typeof(Image), typeof(Button));
            host.transform.SetParent(body, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), 0.22f, 0.50f, 0.78f, 0.98f);
            var hostHit = host.GetComponent<Image>();
            hostHit.sprite = UiFoundation.WhiteSprite();
            hostHit.color = Color.clear;
            hostHit.raycastTarget = true;
            _profilePortrait = AvatarPortraitView.CreateFullBodyFill(host.transform, hideBackground: true,
                badgeCrop: false);
            host.GetComponent<Button>().onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                OpenCustomizer(_onSaved);
            });

            var pip = new GameObject("LevelPip", typeof(RectTransform));
            pip.transform.SetParent(host.transform, false);
            FloatingPanel.Place(pip.GetComponent<RectTransform>(), 0.70f, 0.02f, 0.98f, 0.22f);
            _profileLevel = StyleLevelPip(pip.transform);

            _companionHost = new GameObject("Companions", typeof(RectTransform)).GetComponent<RectTransform>();
            _companionHost.SetParent(body, false);
            FloatingPanel.Place(_companionHost, 0.01f, 0.50f, 0.21f, 0.98f);

            _soulBadge = SoulBadgeView.Create(body);
            FloatingPanel.Place(_soulBadge.GetComponent<RectTransform>(), 0.84f, 0.82f, 0.98f, 0.98f);

            var teamIconGo = new GameObject("TeamIcon", typeof(RectTransform), typeof(Image));
            teamIconGo.transform.SetParent(body, false);
            FloatingPanel.Place(teamIconGo.GetComponent<RectTransform>(), 0.04f, 0.40f, 0.14f, 0.48f);
            _profileTeamIcon = teamIconGo.GetComponent<Image>();
            _profileTeamIcon.sprite = ImagineAssets.IconSlotLook();
            _profileTeamIcon.preserveAspect = true;
            _profileTeamIcon.raycastTarget = false;
            _profileTeamIcon.color = Color.white;

            _profileTitle = FloatingPanel.Body(body, "", 15);
            FloatingPanel.Place(_profileTitle.rectTransform, 0.16f, 0.40f, 0.96f, 0.48f);
            StyleReadable(_profileTitle, 16, DuelystUi.TextCream, TextAnchor.MiddleLeft);

            _profileWallet = FloatingPanel.Body(body, "", 14);
            FloatingPanel.Place(_profileWallet.rectTransform, 0.04f, 0.34f, 0.96f, 0.40f);
            StyleReadable(_profileWallet, 15, DuelystUi.Cyan, TextAnchor.MiddleCenter);

            var xpHost = new GameObject("XpBar", typeof(RectTransform), typeof(Image));
            xpHost.transform.SetParent(body, false);
            FloatingPanel.Grid.Full(xpHost.GetComponent<RectTransform>(), 0.28f, 0.34f);
            var xpBg = xpHost.GetComponent<Image>();
            xpBg.sprite = UiFoundation.WhiteSprite();
            xpBg.color = new Color(0.04f, 0.06f, 0.10f, 0.88f);
            xpBg.raycastTarget = false;
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(xpHost.transform, false);
            var frt = fillGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0.01f, 1f);
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            _profileXpFill = fillGo.GetComponent<Image>();
            _profileXpFill.sprite = UiFoundation.WhiteSprite();
            _profileXpFill.color = GoTheme.LevelGold;
            _profileXpFill.raycastTarget = false;
            _profileXp = FloatingPanel.Body(xpHost.transform, "", 13);
            FloatingPanel.Stretch(_profileXp.rectTransform, 4f);
            StyleReadable(_profileXp, 14, Color.white, TextAnchor.MiddleCenter);

            _statDuels = StatChip(body, "DUELS", ImagineAssets.IconDuel());
            _statCards = StatChip(body, "CARDS", ImagineAssets.IconDeck());
            _statPath = StatChip(body, "KM", ImagineAssets.IconCompass());
            _statSe = StatChip(body, "SE", ImagineAssets.IconSetEnergy());
            FloatingPanel.Grid.Quad(
                _statDuels.transform.parent.GetComponent<RectTransform>(),
                _statCards.transform.parent.GetComponent<RectTransform>(),
                _statPath.transform.parent.GetComponent<RectTransform>(),
                _statSe.transform.parent.GetComponent<RectTransform>(),
                0.12f, 0.26f);

            var customize = HubChrome.Capsule(body, "CUSTOMIZE LOOK", () =>
            {
                FreeUiKit.PlaySelect();
                OpenCustomizer(_onSaved);
            }, MenuCommandButton.Kind.Gold, centerTitle: true, titleSize: 18);
            FloatingPanel.Grid.Full(customize.GetComponent<RectTransform>(), 0.02f, 0.10f);
        }

        static Text StatChip(Transform parent, string caption, Sprite icon)
        {
            var btn = MenuCommandButton.Create(parent, "0", null,
                MenuCommandButton.Kind.Secondary, blurb: caption, centerTitle: false);
            btn.name = "Stat_" + caption;
            MenuCommandButton.ApplyHubType(btn, 16, DuelystUi.GoldHot, displayTitle: false,
                blurbSize: 13, blurbColor: DuelystUi.TextCream);
            var title = btn.transform.Find("Title")?.GetComponent<Text>();
            if (title != null)
            {
                title.resizeTextForBestFit = false;
                title.horizontalOverflow = HorizontalWrapMode.Overflow;
                title.verticalOverflow = VerticalWrapMode.Overflow;
            }

            var blurbT = btn.transform.Find("Blurb")?.GetComponent<Text>();
            if (blurbT != null)
            {
                blurbT.resizeTextForBestFit = false;
                blurbT.horizontalOverflow = HorizontalWrapMode.Overflow;
                blurbT.verticalOverflow = VerticalWrapMode.Overflow;
            }

            if (icon != null)
            {
                var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(btn.transform, false);
                var ir = ico.GetComponent<RectTransform>();
                ir.anchorMin = new Vector2(0.06f, 0.38f);
                ir.anchorMax = new Vector2(0.28f, 0.90f);
                ir.offsetMin = Vector2.zero;
                ir.offsetMax = Vector2.zero;
                var ii = ico.GetComponent<Image>();
                ii.sprite = icon;
                ii.preserveAspect = true;
                ii.raycastTarget = false;
                if (title != null)
                    FloatingPanel.Place(title.rectTransform, 0.30f, 0.42f, 0.96f, 0.94f);
            }

            return title;
        }

        static void StyleReadable(Text t, int size, Color color, TextAnchor align, bool display = false)
        {
            if (t == null) return;
            if (display) WrldzType.StyleGoldTitle(t, size);
            else WrldzType.StyleButtonLabel(t, size);
            t.color = color;
            t.alignment = align;
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        static Text StyleLevelPip(Transform pip)
        {
            var disc = DuelystUi.BtnCircle() ?? UiFoundation.WhiteSprite();
            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(pip, false);
            GoTheme.Stretch(ring.GetComponent<RectTransform>());
            var ringImg = ring.GetComponent<Image>();
            ringImg.sprite = disc;
            ringImg.preserveAspect = true;
            ringImg.color = DuelystUi.Cyan;
            ringImg.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(pip, false);
            GoTheme.Place(fill.GetComponent<RectTransform>(), 0.12f, 0.12f, 0.88f, 0.88f);
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = disc;
            fillImg.preserveAspect = true;
            fillImg.color = new Color(0.08f, 0.14f, 0.24f, 1f);
            fillImg.raycastTarget = false;

            var t = GoTheme.Label(pip, "Lv", "1", 16, Color.white, TextAnchor.MiddleCenter, bold: true);
            StyleReadable(t, 16, Color.white, TextAnchor.MiddleCenter);
            GoTheme.Place(t.rectTransform, 0.12f, 0.12f, 0.88f, 0.88f);
            return t;
        }

        void BuildCustomizerSheet()
        {
            _customizerSheet = new GameObject("CustomizerSheet", typeof(RectTransform));
            _customizerSheet.transform.SetParent(_canvasRoot, false);
            GoTheme.Stretch(_customizerSheet.GetComponent<RectTransform>());

            var frame = DualMenuPresenter.BuildFrame(
                _customizerSheet.transform, "CUSTOMIZE", "Body · hair · colours · wardrobe", () =>
                {
                    FreeUiKit.PlayClick();
                    OpenProfile(_onSaved);
                }, dense: true);
            var body = frame.BodyHost;

            _nameField = MakeInput(body, "NameField", 0.02f, 0.90f, 0.98f, 0.99f);
            _nameRt = _nameField.GetComponent<RectTransform>();

            // 2D portrait stays as the fallback when the 3D base is not in the build
            _preview = AvatarPortraitView.CreateFullBody(body, 0.22f, 0.48f, 0.78f, 0.88f,
                hideBackground: true, badgeCrop: false);
            _preview3d = AvatarPreview3d.Create(body);
            _previewRt = _preview3d.GetComponent<RectTransform>();

            _chipsHost = new GameObject("Chips", typeof(RectTransform)).GetComponent<RectTransform>();
            _chipsHost.SetParent(body, false);
            BuildPartChips(_chipsHost);

            _optionLabel = FloatingPanel.Body(body, "", 14);
            _labelRt = _optionLabel.rectTransform;
            StyleReadable(_optionLabel, 15, DuelystUi.GoldHot, TextAnchor.MiddleCenter);

            BuildOptionStrip(body);

            _controlsHost = new GameObject("Controls", typeof(RectTransform)).GetComponent<RectTransform>();
            _controlsHost.SetParent(body, false);

            _status = FloatingPanel.Body(body, "", 13);
            _statusRt = _status.rectTransform;
            StyleReadable(_status, 14, DuelystUi.TextMuted, TextAnchor.MiddleCenter);

            var save = HubChrome.Capsule(body, "SAVE LOOK", Save, MenuCommandButton.Kind.Gold,
                centerTitle: true, titleSize: 16);
            var back = HubChrome.Capsule(body, "BACK", () =>
            {
                FreeUiKit.PlayClick();
                OpenProfile(_onSaved);
            }, MenuCommandButton.Kind.Secondary, centerTitle: true, titleSize: 16);
            _saveRt = save.GetComponent<RectTransform>();
            _backRt = back.GetComponent<RectTransform>();

            body.gameObject.AddComponent<RectChangeRelay>().Changed = () => Relayout(false);
            Relayout(true);
        }

        /// <summary>
        /// Landscape (the Editor's 1920x1080 Game view, tablets): 3D body on the left third, editors on the right.
        /// Portrait phone: stacked. Re-run whenever the body well changes size.
        /// </summary>
        void Relayout(bool force)
        {
            if (_nameRt == null) return;
            var host = (RectTransform)_nameRt.parent;
            var r = host.rect;
            var wide = r.width > r.height * 1.15f;
            if (!force && wide == _wide) return;
            _wide = wide;
            if (wide)
            {
                GoTheme.Place(_previewRt, 0.01f, 0.13f, 0.37f, 0.99f);
                GoTheme.Place(_preview.GetComponent<RectTransform>(), 0.01f, 0.13f, 0.37f, 0.99f);
                GoTheme.Place(_saveRt, 0.01f, 0.01f, 0.185f, 0.11f);
                GoTheme.Place(_backRt, 0.195f, 0.01f, 0.37f, 0.11f);
                GoTheme.Place(_nameRt, 0.39f, 0.915f, 0.99f, 0.99f);
                GoTheme.Place(_chipsHost, 0.39f, 0.70f, 0.99f, 0.905f);
                GoTheme.Place(_labelRt, 0.39f, 0.645f, 0.99f, 0.695f);
                GoTheme.Place(_stripRt, 0.39f, 0.495f, 0.99f, 0.64f);
                GoTheme.Place(_controlsHost, 0.39f, 0.07f, 0.99f, 0.485f);
                GoTheme.Place(_statusRt, 0.39f, 0.005f, 0.99f, 0.06f);
            }
            else
            {
                GoTheme.Place(_nameRt, 0.02f, 0.925f, 0.98f, 0.99f);
                GoTheme.Place(_previewRt, 0.12f, 0.585f, 0.88f, 0.92f);
                GoTheme.Place(_preview.GetComponent<RectTransform>(), 0.22f, 0.585f, 0.78f, 0.92f);
                GoTheme.Place(_chipsHost, 0.02f, 0.425f, 0.98f, 0.58f);
                GoTheme.Place(_labelRt, 0.02f, 0.39f, 0.98f, 0.42f);
                GoTheme.Place(_stripRt, 0.02f, 0.30f, 0.98f, 0.385f);
                GoTheme.Place(_controlsHost, 0.02f, 0.125f, 0.98f, 0.295f);
                GoTheme.Place(_statusRt, 0.02f, 0.10f, 0.98f, 0.123f);
                FloatingPanel.Grid.Pair(_saveRt, _backRt, 0.01f, 0.095f);
            }
            if (_customizerSheet != null && _customizerSheet.activeInHierarchy) RebuildOptionStrip();
        }

        static readonly Part[] ChipOrder =
        {
            Part.Body, Part.FaceShape, Part.Hair, Part.HairColor, Part.Skin, Part.Outfit, Part.Paint, Part.Look, Part.Title, Part.Accent,
            Part.Hat, Part.Facial, Part.Neck, Part.Shirt, Part.Hands, Part.Bottoms, Part.Shoes, Part.Accessories, Part.Tattoos, Part.Disk, Part.Aura
        };

        static string ChipLabel(Part p) => p switch
        {
            Part.Body => "BODY", Part.FaceShape => "FACE SHAPE", Part.Hair => "HAIR", Part.HairColor => "HAIR DYE", Part.Skin => "SKIN",
            Part.Paint => "CLOTHES", Part.Outfit => "OUTFIT", Part.Look => "PORTRAIT", Part.Title => "TITLE", Part.Accent => "SPIRIT",
            Part.Hat => "HAT", Part.Facial => "FACE", Part.Shirt => "SHIRT", Part.Hands => "HANDS",
            Part.Bottoms => "BOTTOMS", Part.Shoes => "SHOES", Part.Neck => "NECK", Part.Accessories => "ACCESSORIES", Part.Tattoos => "TATTOOS",
            Part.Disk => "DISK", Part.Aura => "AURA", _ => ""
        };

        void BuildPartChips(Transform sheet)
        {
            _slotImgs.Clear();
            _slotRings.Clear();
            _slotParts.Clear();
            const int cols = 9;
            const float gap = 0.010f, rowGap = 0.06f;
            var cellW = (1f - gap * (cols - 1)) / cols;
            var rows = (ChipOrder.Length + cols - 1) / cols;
            var rowH = (1f - rowGap * (rows - 1)) / rows;
            for (var i = 0; i < ChipOrder.Length; i++)
            {
                var p = ChipOrder[i];
                var col = i % cols;
                var row = i / cols;
                var xx0 = col * (cellW + gap);
                var yy1 = 1f - row * (rowH + rowGap);
                var chip = new GameObject("Slot_" + p, typeof(RectTransform), typeof(Image), typeof(Button));
                chip.transform.SetParent(sheet, false);
                GoTheme.Place(chip.GetComponent<RectTransform>(), xx0, yy1 - rowH, xx0 + cellW, yy1);
                var bg = chip.GetComponent<Image>();
                bg.sprite = UiFoundation.WhiteSprite();
                bg.color = new Color(0.04f, 0.06f, 0.10f, 0.55f);

                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(Outline));
                iconGo.transform.SetParent(chip.transform, false);
                GoTheme.Place(iconGo.GetComponent<RectTransform>(), 0.12f, 0.30f, 0.88f, 0.97f);
                var img = iconGo.GetComponent<Image>();
                img.sprite = SlotIcon(p);
                img.preserveAspect = true;
                img.raycastTarget = false;
                var ring = chip.AddComponent<Outline>();
                ring.effectColor = new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.95f);
                ring.effectDistance = new Vector2(2f, -2f);
                ring.useGraphicAlpha = false;
                ring.enabled = false;
                Destroy(iconGo.GetComponent<Outline>());

                var lbl = FloatingPanel.Body(chip.transform, ChipLabel(p), 10);
                GoTheme.Place(lbl.rectTransform, 0f, 0f, 1f, 0.30f);
                StyleReadable(lbl, 10, DuelystUi.TextCream, TextAnchor.MiddleCenter);
                lbl.resizeTextForBestFit = true; lbl.resizeTextMinSize = 7; lbl.resizeTextMaxSize = 12;
                lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
                lbl.raycastTarget = false;

                var captured = p;
                chip.GetComponent<Button>().onClick.AddListener(() =>
                {
                    FreeUiKit.PlaySelect();
                    _focus = captured;
                    UpdateOptionLabel();
                    RebuildOptionStrip();
                    RefreshSlotRings();
                });
                _slotImgs.Add(img);
                _slotRings.Add(ring);
                _slotParts.Add(p);
            }
        }

        void BuildOptionStrip(Transform sheet)
        {
            var strip = new GameObject("OptionStrip", typeof(RectTransform), typeof(Image), typeof(ScrollRect),
                typeof(RectMask2D));
            strip.transform.SetParent(sheet, false);
            _stripRt = strip.GetComponent<RectTransform>();
            _optionHost = MakeScrollStrip(strip);
            _stripHost = _optionHost;
            // wardrobe tabs in the wide layout: a two-row scrolling grid over the strip + the (unused) controls area, so a
            // slot's pieces are visible at once with legible names (the single strip showed 10 of 19 shirts at 720p)
            var grid = new GameObject("WardrobeGrid", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            grid.transform.SetParent(sheet, false);
            _gridRt = grid.GetComponent<RectTransform>();
            _gridHost = MakeScrollStrip(grid);
            var hl = _gridHost.GetComponent<HorizontalLayoutGroup>();
            if (hl != null) DestroyImmediate(hl);
            var gl = _gridHost.gameObject.AddComponent<GridLayoutGroup>();
            gl.startAxis = GridLayoutGroup.Axis.Vertical;
            gl.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            gl.constraintCount = 2;
            gl.spacing = new Vector2(10f, 10f);
            gl.padding = new RectOffset(8, 8, 6, 6);
            gl.childAlignment = TextAnchor.UpperLeft;
            // "more" cue at the right edge while tiles lie past it (a dark fade strip + chevron, never a raycast target)
            var cue = new GameObject("MoreCue", typeof(RectTransform), typeof(Image));
            cue.transform.SetParent(grid.transform, false);
            var cueRt = cue.GetComponent<RectTransform>();
            cueRt.anchorMin = new Vector2(1f, 0f); cueRt.anchorMax = new Vector2(1f, 1f); cueRt.pivot = new Vector2(1f, 0.5f);
            cueRt.sizeDelta = new Vector2(34f, 0f); cueRt.anchoredPosition = Vector2.zero;
            var cueImg = cue.GetComponent<Image>(); cueImg.sprite = UiFoundation.WhiteSprite();
            cueImg.color = new Color(0.03f, 0.04f, 0.08f, 0.72f); cueImg.raycastTarget = false;
            var chev = FloatingPanel.Body(cue.transform, "\u203A", 34);
            chev.alignment = TextAnchor.MiddleCenter;
            var chevRt = chev.GetComponent<RectTransform>(); chevRt.anchorMin = Vector2.zero; chevRt.anchorMax = Vector2.one;
            chevRt.offsetMin = chevRt.offsetMax = Vector2.zero;
            cue.SetActive(false);
            var fit = grid.AddComponent<WholeColumnGrid>();
            fit.Grid = gl; fit.Viewport = _gridRt; fit.Scroll = grid.GetComponent<ScrollRect>(); fit.Cue = cue;
            grid.SetActive(false);
        }

        Transform _stripHost, _gridHost;
        RectTransform _gridRt;

        /// <summary>Horizontal scrolling tile row on an existing GameObject (Image + ScrollRect + RectMask2D).</summary>
        static Transform MakeScrollStrip(GameObject strip)
        {
            var bg = strip.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = new Color(0.04f, 0.06f, 0.10f, 0.42f);
            bg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(strip.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 0.5f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, 0f);
            var h = content.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.padding = new RectOffset(6, 6, 4, 4);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = false;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            content.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = strip.GetComponent<ScrollRect>();
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.viewport = strip.GetComponent<RectTransform>();
            sr.content = crt;
            return content.transform;
        }

        void ApplySwatch(int idx)
        {
            FreeUiKit.PlayClick();
            string hex = null;
            switch (_focus)
            {
                case Part.Skin:
                    hex = AvatarCatalog.SkinPresets[Mathf.Clamp(idx, 0, AvatarCatalog.SkinPresets.Length - 1)];
                    _draft.skinHex = hex;
                    break;
                case Part.HairColor:
                    hex = AvatarCatalog.HairPresets[Mathf.Clamp(idx, 0, AvatarCatalog.HairPresets.Length - 1)];
                    _draft.hairHex = hex;
                    break;
                case Part.Accent:
                    hex = AvatarCatalog.AccentPresets[Mathf.Clamp(idx, 0, AvatarCatalog.AccentPresets.Length - 1)];
                    _draft.accentHex = hex;
                    break;
                case Part.OutfitTint:
                    hex = AvatarCatalog.AccentPresets[Mathf.Clamp(idx, 0, AvatarCatalog.AccentPresets.Length - 1)];
                    _draft.outfitTintHex = hex;
                    break;
                case Part.Disk:
                    _draft.diskHex = AvatarCatalog.DiskPalettes[Mathf.Clamp(idx, 0, AvatarCatalog.DiskPalettes.Length - 1)];
                    break;
                case Part.Aura:
                    _draft.auraHex = AvatarCatalog.AuraPalettes[Mathf.Clamp(idx, 0, AvatarCatalog.AuraPalettes.Length - 1)];
                    break;
                default:
                    // If not on a color part, treat as accent
                    hex = AvatarCatalog.AccentPresets[Mathf.Clamp(idx, 0, AvatarCatalog.AccentPresets.Length - 1)];
                    _draft.accentHex = hex;
                    break;
            }

            ApplyPreview();
            UpdateOptionLabel();
            RebuildOptionStrip();
        }

        static readonly Dictionary<string, Sprite> s_clothThumbs = new();

        /// <summary>Wardrobe tile picture: the piece rendered on this body (thumb_&lt;id&gt;_&lt;sex&gt;.png), else null.</summary>
        static Sprite ClothThumb(string id, bool female)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var key = id + (female ? "_female" : "_male");
            if (s_clothThumbs.TryGetValue(key, out var sp)) return sp;
            var tex = Resources.Load<Texture2D>(OverworldAvatar3d.ClothRes + "thumb_" + key)
                      ?? Resources.Load<Texture2D>(OverworldAvatar3d.ClothRes + "thumb_" + id + (female ? "_male" : "_female"));
            sp = tex != null ? Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            s_clothThumbs[key] = sp;
            return sp;
        }

        static Sprite SlotIcon(Part part) => part switch
        {
            Part.Look => ImagineAssets.IconSlotLook(),
            Part.Hat => ImagineAssets.IconSlotHat(),
            Part.Facial => ImagineAssets.IconSlotFacial(),
            Part.Shirt => ImagineAssets.IconSlotShirt(),
            Part.Hands => ImagineAssets.IconSlotHands(),
            Part.Bottoms => ImagineAssets.IconSlotBottoms(),
            Part.Shoes => ImagineAssets.IconSlotShoes(),
            Part.Neck => ImagineAssets.IconSlotAccent(),
            Part.Accessories => ImagineAssets.IconSlotHands(),
            Part.Tattoos => ImagineAssets.IconSlotFacial(),
            Part.Accent => ImagineAssets.IconSlotAccent(),
            Part.Title => ImagineAssets.IconSlotTitle(),
            Part.Disk => ImagineAssets.IconDuel(),
            Part.Aura => ImagineAssets.IconSoul(),
            Part.Body => ImagineAssets.IconSlotLook(),
            Part.Hair => ImagineAssets.IconSlotHat(),
            Part.HairColor => ImagineAssets.IconSlotAccent(),
            Part.Skin => ImagineAssets.IconSlotFacial(),
            Part.FaceShape => ImagineAssets.IconSlotFacial(),
            Part.Paint => ImagineAssets.IconSlotShirt(),
            Part.Outfit => ImagineAssets.IconSlotShirt(),
            _ => ImagineAssets.IconSlotLook()
        };

        void RefreshSlotRings()
        {
            for (var i = 0; i < _slotRings.Count; i++)
            {
                if (_slotRings[i] == null) continue;
                var on = i < _slotParts.Count && _slotParts[i] == _focus;
                _slotRings[i].enabled = on;
                if (_slotImgs[i] != null)
                    _slotImgs[i].color = on ? Color.white : new Color(0.78f, 0.80f, 0.84f, 0.92f);
            }
        }

        void RebuildOptionStrip()
        {
            if (_optionHost == null || _draft == null) return;
            FloatingPanel.DestroyChildrenNow(_optionHost);
            if (_controlsHost != null) FloatingPanel.DestroyChildrenNow(_controlsHost);
            // wardrobe tabs have no slider block under the strip: in the wide layout they use the two-row grid
            var wardrobe = _focus is Part.Hat or Part.Facial or Part.Shirt or Part.Hands or Part.Bottoms or Part.Shoes or Part.Neck;
            var useGrid = wardrobe && _wide && _gridHost != null;
            if (_gridHost != null) FloatingPanel.DestroyChildrenNow(_gridHost);
            if (_stripHost != null) FloatingPanel.DestroyChildrenNow(_stripHost);
            if (_gridRt != null)
            {
                _gridRt.gameObject.SetActive(useGrid);
                if (useGrid)
                {
                    GoTheme.Place(_gridRt, 0.39f, 0.07f, 0.99f, 0.64f);
                    Canvas.ForceUpdateCanvases();
                    var h = Mathf.Max(120f, _gridRt.rect.height);
                    var cellH = (h - 12f - 10f) * 0.5f;
                    var g = _gridHost.GetComponent<GridLayoutGroup>();
                    g.cellSize = new Vector2(Mathf.Round(cellH * 0.80f), Mathf.Round(cellH));
                    var sr = _gridRt.GetComponent<ScrollRect>(); if (sr != null) sr.horizontalNormalizedPosition = 0f;
                }
            }
            if (_stripRt != null) _stripRt.gameObject.SetActive(!useGrid);
            if (_stripHost != null) _optionHost = useGrid ? _gridHost : _stripHost;

            if (_focus is Part.Body or Part.Hair or Part.HairColor or Part.Paint or Part.Skin or Part.Outfit or Part.FaceShape or Part.Accessories or Part.Tattoos)
            {
                BuildEditor3d();
                RefreshSlotRings();
                return;
            }

            if (_focus == Part.Look)
            {
                for (var i = 0; i < AvatarCatalog.PortraitCount; i++)
                {
                    var idx = i;
                    var spr = StreamingSprite.Load($"WRLDZ/Avatar/portraits/portrait_{i}.png");
                    OptionTile(spr, idx == _draft.portraitIndex, Color.white, () =>
                    {
                        _draft.portraitIndex = idx;
                        ApplyPreview();
                        UpdateOptionLabel();
                        RebuildOptionStrip();
                    });
                }
            }
            else if (_focus is Part.Hat or Part.Facial or Part.Shirt or Part.Hands or Part.Bottoms or Part.Shoes or Part.Neck)
            {
                var slot = _focus switch
                {
                    Part.Hat => ClothingSlot.Hat,
                    Part.Facial => ClothingSlot.Facial,
                    Part.Shirt => ClothingSlot.Shirt,
                    Part.Hands => ClothingSlot.Hands,
                    Part.Bottoms => ClothingSlot.Bottoms,
                    Part.Neck => ClothingSlot.Neck,
                    _ => ClothingSlot.Shoes
                };
                var owned = ClothingCatalog.OwnedInSlot(_wardrobe, slot, _draft.IsFemale);
                var cur = _draft.SlotId(slot);
                for (var i = 0; i < owned.Count; i++)
                {
                    var item = owned[i];
                    // a thumbnail of the 3D piece on the current body (Clothes/thumb_<id>_<sex>.png, tools/custom/clothes_thumbs.py);
                    // the empty slots keep the slot glyph; anything without a thumb falls back to its paper-doll art
                    // empty slots show the bare body part (thumb_hat_none_* ...), not a glyph of the item they remove
                    var spr = item.EmptySlot ? (ClothThumb(item.Id, _draft.IsFemale) ?? SlotIcon(_focus))
                        : ClothThumb(item.Id, _draft.IsFemale)
                          ?? (!string.IsNullOrEmpty(item.ArtPath) ? StreamingSprite.Load(item.ArtPath) : null)
                          ?? SlotIcon(_focus);
                    OptionTile(_optionHost, spr, item.Id == cur, Color.white, () =>
                    {
                        _draft.SetSlot(slot, item.Id);
                        ApplyPreview();
                        UpdateOptionLabel();
                        RebuildOptionStrip();
                    }, item.Name, 128f);
                }
            }
            else if (_focus == Part.Accent)
            {
                for (var i = 0; i < AvatarCatalog.AccentPresets.Length; i++)
                {
                    var idx = i;
                    var hex = AvatarCatalog.AccentPresets[i];
                    var on = string.Equals(_draft.accentHex, hex, StringComparison.OrdinalIgnoreCase);
                    OptionTile(UiFoundation.WhiteSprite(), on, AvatarPortraitView.Parse(hex, Color.gray), () =>
                    {
                        _draft.accentHex = hex;
                        ApplyPreview();
                        UpdateOptionLabel();
                        RebuildOptionStrip();
                    });
                }
            }
            else if (_focus == Part.Disk || _focus == Part.Aura)
            {
                // Spirit Dueler palettes. Swatch 0 ("") follows the Spirit magic colour, as disks did before palettes.
                var disk = _focus == Part.Disk;
                var hexes = disk ? AvatarCatalog.DiskPalettes : AvatarCatalog.AuraPalettes;
                var cur = (disk ? _draft.diskHex : _draft.auraHex) ?? "";
                var spirit = AvatarPortraitView.Parse(_draft.accentHex, Color.cyan);
                for (var i = 0; i < hexes.Length; i++)
                {
                    var hex = hexes[i];
                    var on = string.Equals(cur, hex, StringComparison.OrdinalIgnoreCase);
                    var tint = hex.Length == 0 ? spirit : AvatarPortraitView.Parse(hex, Color.gray);
                    OptionTile(hex.Length == 0 ? SlotIcon(_focus) : UiFoundation.WhiteSprite(), on, tint, () =>
                    {
                        if (disk) _draft.diskHex = hex;
                        else _draft.auraHex = hex;
                        ApplyPreview();
                        UpdateOptionLabel();
                        RebuildOptionStrip();
                    });
                }
            }
            else if (_focus == Part.Title)
            {
                for (var i = 0; i < AvatarCatalog.PresetTitles.Length; i++)
                {
                    var idx = i;
                    var on = _draft.title == AvatarCatalog.PresetTitles[i];
                    OptionTile(ImagineAssets.IconSlotTitle(), on, Color.white, () =>
                    {
                        _draft.title = AvatarCatalog.PresetTitles[idx];
                        ApplyPreview();
                        UpdateOptionLabel();
                        RebuildOptionStrip();
                    });
                }
            }

            RefreshSlotRings();
        }

        void OptionTile(Sprite spr, bool on, Color tint, Action click) => OptionTile(_optionHost, spr, on, tint, click);

        GameObject OptionTile(Transform host, Sprite spr, bool on, Color tint, Action click, string label = null, float width = 84f)
        {
            var go = new GameObject("Opt", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement),
                typeof(Outline));
            go.transform.SetParent(host, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            // the strip's HorizontalLayoutGroup does not control child width: size the rect itself
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, 0f);
            le.flexibleWidth = 0f;
            var img = go.GetComponent<Image>();
            img.sprite = spr ?? UiFoundation.WhiteSprite();
            img.preserveAspect = img.sprite != UiFoundation.WhiteSprite();   // colour swatches fill the tile
            img.color = tint;
            img.raycastTarget = true;
            var ring = go.GetComponent<Outline>();
            ring.effectColor = new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.95f);
            ring.effectDistance = new Vector2(1.8f, -1.8f);
            ring.useGraphicAlpha = false;
            ring.enabled = on;
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                click?.Invoke();
            });
            if (!string.IsNullOrEmpty(label))
            {
                // wardrobe tile: the tile's own Image becomes a plain backing, the picture moves into a child drawn after the
                // selection wash (UGUI draws children over their parent, so a wash child of the picture covered it)
                ring.enabled = false;                                   // the Outline only echoed the sprite's silhouette
                img.sprite = UiFoundation.WhiteSprite(); img.preserveAspect = false;
                img.color = on ? new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.85f) : new Color(0.06f, 0.08f, 0.14f, 0.55f);
                var inner = new GameObject("Backing", typeof(RectTransform), typeof(Image));
                inner.transform.SetParent(go.transform, false);
                var irt = inner.GetComponent<RectTransform>(); irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
                var bw = on ? 3f : 1f; irt.offsetMin = new Vector2(bw, bw); irt.offsetMax = new Vector2(-bw, -bw);
                var ii = inner.GetComponent<Image>(); ii.sprite = UiFoundation.WhiteSprite(); ii.raycastTarget = false;
                ii.color = on ? new Color(0.20f, 0.17f, 0.10f, 1f) : new Color(0.07f, 0.09f, 0.16f, 1f);
                var pic = new GameObject("Picture", typeof(RectTransform), typeof(Image));
                pic.transform.SetParent(go.transform, false);
                GoTheme.Place(pic.GetComponent<RectTransform>(), 0.03f, 0.30f, 0.97f, 0.98f);
                var pi = pic.GetComponent<Image>(); pi.sprite = spr ?? UiFoundation.WhiteSprite(); pi.preserveAspect = true;
                pi.color = tint; pi.raycastTarget = false;
                var t = FloatingPanel.Body(go.transform, label, 12);
                GoTheme.Place(t.rectTransform, 0.02f, 0f, 0.98f, 0.32f);
                StyleReadable(t, 14, DuelystUi.TextCream, TextAnchor.MiddleCenter);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = 18;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.raycastTarget = false;
                // (no shade: the opaque backing already sits under the label)
                t.transform.SetAsLastSibling();
            }
            return go;
        }

        void Cycle(int dir)
        {
            FreeUiKit.PlaySelect();
            switch (_focus)
            {
                case Part.Look:
                    _draft.portraitIndex = Wrap(_draft.portraitIndex + dir, AvatarCatalog.PortraitCount);
                    break;
                case Part.Hat:
                    CycleClothing(ClothingSlot.Hat, dir);
                    break;
                case Part.Facial:
                    CycleClothing(ClothingSlot.Facial, dir);
                    break;
                case Part.Shirt:
                    CycleClothing(ClothingSlot.Shirt, dir);
                    break;
                case Part.Hands:
                    CycleClothing(ClothingSlot.Hands, dir);
                    break;
                case Part.Bottoms:
                    CycleClothing(ClothingSlot.Bottoms, dir);
                    break;
                case Part.Shoes:
                    CycleClothing(ClothingSlot.Shoes, dir);
                    break;
                case Part.Neck:
                    CycleClothing(ClothingSlot.Neck, dir);
                    break;
                case Part.Title:
                    var ti = Array.IndexOf(AvatarCatalog.PresetTitles, _draft.title);
                    if (ti < 0) ti = 0;
                    ti = Wrap(ti + dir, AvatarCatalog.PresetTitles.Length);
                    _draft.title = AvatarCatalog.PresetTitles[ti];
                    break;
                case Part.Skin:
                    CycleHex(ref _draft.skinHex, AvatarCatalog.SkinPresets, dir);
                    break;
                case Part.HairColor:
                    CycleHex(ref _draft.hairHex, AvatarCatalog.HairPresets, dir);
                    break;
                case Part.OutfitTint:
                    CycleHex(ref _draft.outfitTintHex, AvatarCatalog.AccentPresets, dir);
                    break;
                case Part.Disk:
                    CycleHex(ref _draft.diskHex, AvatarCatalog.DiskPalettes, dir);
                    break;
                case Part.Aura:
                    CycleHex(ref _draft.auraHex, AvatarCatalog.AuraPalettes, dir);
                    break;
                case Part.Accent:
                    CycleHex(ref _draft.accentHex, AvatarCatalog.AccentPresets, dir);
                    break;
            }

            ApplyPreview();
            UpdateOptionLabel();
            RebuildOptionStrip();
        }

        void CycleClothing(ClothingSlot slot, int dir)
        {
            var owned = ClothingCatalog.OwnedInSlot(_wardrobe, slot, _draft.IsFemale);
            if (owned == null || owned.Count == 0) return;
            var cur = _draft.SlotId(slot);
            var i = 0;
            for (var k = 0; k < owned.Count; k++)
            {
                if (owned[k].Id != cur) continue;
                i = k;
                break;
            }

            i = Wrap(i + dir, owned.Count);
            _draft.SetSlot(slot, owned[i].Id);
        }

        static void CycleHex(ref string hex, string[] presets, int dir)
        {
            var i = 0;
            for (var k = 0; k < presets.Length; k++)
                if (string.Equals(presets[k], hex, StringComparison.OrdinalIgnoreCase))
                {
                    i = k;
                    break;
                }

            i = Wrap(i + dir, presets.Length);
            hex = presets[i];
        }

        static int Wrap(int v, int count)
        {
            if (count <= 0) return 0;
            v %= count;
            if (v < 0) v += count;
            return v;
        }

        void UpdateOptionLabel()
        {
            if (_optionLabel == null || _draft == null) return;
            var pi = Mathf.Clamp(_draft.portraitIndex, 0, AvatarCatalog.PortraitCount - 1);
            _optionLabel.text = _focus switch
            {
                Part.Look => $"Look: {AvatarCatalog.PortraitNames[pi]}",
                Part.Hat => ClothingLabel(ClothingSlot.Hat),
                Part.Facial => ClothingLabel(ClothingSlot.Facial),
                Part.Shirt => ClothingLabel(ClothingSlot.Shirt),
                Part.Hands => ClothingLabel(ClothingSlot.Hands),
                Part.Bottoms => ClothingLabel(ClothingSlot.Bottoms),
                Part.Shoes => ClothingLabel(ClothingSlot.Shoes),
                Part.Neck => ClothingLabel(ClothingSlot.Neck),
                Part.Skin => "Skin: " + (string.IsNullOrEmpty(_draft.GetPaint("Body.0")) ? "original" : _draft.GetPaint("Body.0")),
                Part.FaceShape => "Face shape: " + FaceShapeWord(),
                Part.Accessories => "Accessories: " + ((_draft.accessoryIds?.Count ?? 0) == 0 ? "none" : _draft.accessoryIds.Count.ToString()),
                Part.Tattoos => "Tattoos: " + ((_draft.tattooIds?.Count ?? 0) == 0 ? "none" : _draft.tattooIds.Count.ToString()),
                Part.HairColor => "Hair color: " + AvatarCatalog.HairPatternNames[Mathf.Clamp(_draft.hairPattern, 0, AvatarCatalog.HairPatternNames.Length - 1)]
                                  + (_draft.hairPattern == 0 ? "" : "  ·  " + _hairZone.ToUpperInvariant()),
                Part.Body => $"{(_draft.IsFemale ? "Female" : "Male")} base  ·  {_draft.EffectiveHeightCm:0} cm  ·  weight {Mathf.RoundToInt(_draft.weight * 100f)}",
                Part.Hair => "Hair: " + HairName(_draft.hairStyleId),
                Part.Outfit => OutfitOptionLabel(),
                Part.Paint => "Clothes color: " + PaintLabel(_paintKey) + "  ·  " + (string.IsNullOrEmpty(_draft.GetPaint(_paintKey)) ? "original" : _draft.GetPaint(_paintKey)),
                Part.OutfitTint => $"Outfit tint: {_draft.outfitTintHex}",
                Part.Accent => $"Spirit magic: {_draft.accentHex}",
                Part.Disk => "Duel Disk: " + AvatarCatalog.PaletteName(AvatarCatalog.DiskPalettes,
                    AvatarCatalog.DiskPaletteNames, _draft.diskHex),
                Part.Aura => "Aura: " + AvatarCatalog.PaletteName(AvatarCatalog.AuraPalettes,
                    AvatarCatalog.AuraPaletteNames, _draft.auraHex),
                Part.Title => $"Title: {_draft.title}",
                _ => ""
            };
            var pockets = ClothingService.PreviewPockets(_draft, _wardrobe);
            if (_status != null)
                _status.text = pockets == 1
                    ? "1 on-hand deck"
                    : pockets + " on-hand decks";
            RefreshSlotRings();
        }

        string ClothingLabel(ClothingSlot slot)
        {
            var item = ClothingCatalog.Get(_draft.SlotId(slot));
            if (item == null) return ClothingCatalog.SlotLabel(slot);
            var n = ClothingCatalog.PocketsOnItem(item, _wardrobe);
            if (item.CanHoldPockets)
                return $"{item.Name}  ·  {n} pocket{(n == 1 ? "" : "s")}";
            return item.Name;
        }

        void ApplyPreview()
        {
            _draft?.ClampToCatalog();
            _preview?.Apply(_draft);
            var has3d = _preview3d != null && _preview3d.Apply(_draft);
            if (_preview != null) _preview.gameObject.SetActive(!has3d);
        }

        void RefreshProfile()
        {
            _profilePortrait?.Apply(_draft);
            RefreshCompanions();
            var acc = AppSession.Ensure().Account;
            if (acc == null)
            {
                if (_profileName != null) _profileName.text = "Not signed in";
                if (_profileTitle != null) _profileTitle.text = "";
                if (_profileHandle != null) _profileHandle.text = "";
                if (_profileWallet != null) _profileWallet.text = "";
                if (_profileTeamIcon != null) _profileTeamIcon.enabled = false;
                return;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();
            var p = acc.progress;
            var lv = p != null ? Mathf.Max(1, p.level) : Mathf.Max(1, acc.spiritRank);
            if (_profileName != null)
                _profileName.text = string.IsNullOrEmpty(acc.displayName) ? acc.username : acc.displayName;
            if (_profileHandle != null)
            {
                var title = string.IsNullOrEmpty(_draft?.title) ? "Spirit Dueler" : _draft.title;
                var handle = string.IsNullOrEmpty(acc.username) ? "" : "@" + acc.username;
                _profileHandle.text = string.IsNullOrEmpty(handle) ? title : handle + "  ·  " + title;
            }

            if (_profileTitle != null)
            {
                var team = p != null ? p.Team : KuribohTeam.None;
                _profileTitle.text = KuribohTeamInfo.DisplayName(team);
            }

            if (_profileWallet != null)
                _profileWallet.text =
                    $"Đ {(p?.digizeni ?? 0):N0}   ·   DC {(p?.duelCoin ?? 0):N0}   ·   SE {(p?.setEnergy ?? 0):N0}";

            _soulBadge?.Bind(p);

            if (_profileTeamIcon != null)
            {
                var team = p != null ? p.Team : KuribohTeam.None;
                var spr = ImagineAssets.IconTeam(team);
                _profileTeamIcon.enabled = spr != null;
                if (spr != null)
                    _profileTeamIcon.sprite = spr;
            }

            if (_profileLevel != null)
                _profileLevel.text = lv.ToString();

            if (p != null && _profileXpFill != null)
            {
                var t = Mathf.Clamp(p.XpProgress01(), 0.02f, 1f);
                var frt = _profileXpFill.rectTransform;
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = new Vector2(t, 1f);
                frt.offsetMin = new Vector2(2f, 2f);
                frt.offsetMax = new Vector2(-2f, -2f);
            }

            if (_profileXp != null && p != null)
            {
                var need = p.XpToNextLevel();
                _profileXp.text = need <= 0
                    ? $"Lv{lv}  MAX"
                    : $"Lv{lv}  {p.xp:N0}/{need:N0}";
            }

            if (_statDuels != null) _statDuels.text = acc.duelsCompleted.ToString("N0");
            if (_statCards != null) _statCards.text = acc.cardsCollected.ToString("N0");
            if (_statPath != null)
                _statPath.text = acc.pathKm < 10f
                    ? acc.pathKm.ToString("0.0")
                    : acc.pathKm.ToString("N0");
            if (_statSe != null) _statSe.text = (p != null ? p.setEnergy : 0).ToString("N0");
        }

        void Save()
        {
            FreeUiKit.PlayConfirm();
            var session = AppSession.Ensure();
            var acc = session.Account;
            if (acc == null)
            {
                if (_status != null) _status.text = "No account session.";
                return;
            }

            _draft.ClampToCatalog();
            if (_nameField != null && !string.IsNullOrWhiteSpace(_nameField.text))
                acc.displayName = _nameField.text.Trim();

            acc.avatar = _draft.Clone();
            acc.avatarColor = _draft.accentHex; // keep legacy field in sync
            ClothingService.EnsureWardrobe(acc);

            if (!LocalAccountStore.TrySaveAccount(acc, out var err))
            {
                if (_status != null) _status.text = err ?? "Save failed.";
                return;
            }

            session.SetAccount(acc);
            WRLDZ.Presentation.ArInteraction.SpiritDuelerPalette.NotifyChanged();
            if (_status != null) _status.text = "Saved! Look applied.";
            _onSaved?.Invoke();
            RefreshProfile();
        }

        GameObject MakeSheet(string name)
        {
            GoTheme.EnsureAssets();
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(_canvasRoot, false);
            GoTheme.Stretch(root.GetComponent<RectTransform>());

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(root.transform, false);
            GoTheme.Stretch(dim.GetComponent<RectTransform>());
            var dImg = dim.GetComponent<Image>();
            dImg.sprite = UiFoundation.WhiteSprite();
            dImg.color = new Color(0.01f, 0.02f, 0.05f, 0.48f);
            dim.GetComponent<Button>().onClick.AddListener(HideAll);

            var sheetGo = new GameObject("Sheet", typeof(RectTransform), typeof(Image));
            sheetGo.transform.SetParent(root.transform, false);
            GoTheme.Place(sheetGo.GetComponent<RectTransform>(), 0.06f, 0.10f, 0.94f, 0.88f);
            var sheetImg = sheetGo.GetComponent<Image>();
            sheetImg.raycastTarget = true;
            HubChrome.PaintWell(sheetImg);
            return root;
        }

        static void RowButton(Transform sheet, string name, string label, float x0, float y0, float x1, float y1,
            Action onClick)
        {
            var primary = name is not ("Back" or "Close");
            GoTheme.RectAction(sheet, name, label, x0, y0, x1, y1, primary, onClick);
        }

        static void CircleNav(Transform sheet, string name, string glyph, float x0, float y0, float x1, float y1,
            Action onClick)
        {
            var spr = name == "Prev"
                ? (VendorKit.Arrow("w") ?? VendorKit.MobileNo() ?? GoTheme.OrbItems())
                : (VendorKit.Arrow("e") ?? VendorKit.MobileYes() ?? GoTheme.OrbItems());
            var btn = GoTheme.CircleButton(sheet, name, spr, Color.white, x0, y0, x1, y1, onClick);
            // fallback glyph if sprite is plain circle
            if (spr == null || spr.name.Contains("button_round"))
                GoTheme.IconLabel(btn.transform, glyph, GoTheme.Ink, 0.6f);
        }

        static InputField MakeInput(Transform sheet, string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(sheet, false);
            GoTheme.Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.sprite = DuelystUi.Bar() ?? UiFoundation.WhiteSprite();
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            img.raycastTarget = true;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            GoTheme.Stretch(textGo.GetComponent<RectTransform>());
            var text = textGo.GetComponent<Text>();
            WrldzType.Style(text, 16, display: false);
            text.color = DuelystUi.TextCream;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var trt = textGo.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(10, 2);
            trt.offsetMax = new Vector2(-10, -2);

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            GoTheme.Stretch(phGo.GetComponent<RectTransform>());
            var ph = phGo.GetComponent<Text>();
            WrldzType.Style(ph, 15, display: false);
            ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(0.5f, 0.52f, 0.56f, 0.8f);
            ph.text = "Display name";
            ph.alignment = TextAnchor.MiddleLeft;
            var prt = phGo.GetComponent<RectTransform>();
            prt.offsetMin = new Vector2(10, 2);
            prt.offsetMax = new Vector2(-10, -2);

            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = 24;
            return field;
        }
    }

    /// <summary>
    /// The wardrobe grid scrolls sideways in two rows. At rest its right edge used to cut a column in half with nothing
    /// saying more tiles followed (review 2026-10-04). This keeps whole columns at the edge (the column spacing absorbs
    /// the leftover width, at least 6 px) and shows a "more" chevron while tiles lie past the right edge.
    /// </summary>
    sealed class WholeColumnGrid : MonoBehaviour
    {
        public GridLayoutGroup Grid;
        public RectTransform Viewport;
        public ScrollRect Scroll;
        public GameObject Cue;
        float _w = -1f, _cell = -1f;
        int _n = -1;

        void LateUpdate()
        {
            if (Grid == null || Viewport == null) return;
            var w = Viewport.rect.width; var n = Grid.transform.childCount; var cell = Grid.cellSize.x;
            if (!Mathf.Approximately(w, _w) || n != _n || !Mathf.Approximately(cell, _cell))
            {
                _w = w; _n = n; _cell = cell;
                var inner = w - Grid.padding.left - Grid.padding.right;
                var cols = Mathf.Max(1, Mathf.FloorToInt((inner + 6f) / (cell + 6f)));
                var sx = cols > 1 ? (inner - cols * cell) / (cols - 1) : 6f;
                Grid.spacing = new Vector2(Mathf.Max(6f, Mathf.Floor(sx)), Grid.spacing.y);
            }
            if (Cue == null) return;
            var content = Scroll != null ? Scroll.content : null;
            var overflow = content != null && content.rect.width > w + 1f;
            var atEnd = Scroll != null && Scroll.horizontalNormalizedPosition > 0.98f;
            if (Cue.activeSelf != (overflow && !atEnd)) Cue.SetActive(overflow && !atEnd);
        }
    }
}
