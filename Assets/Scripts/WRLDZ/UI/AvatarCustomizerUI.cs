using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI.MenuV6;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// The duelist PROFILE and CUSTOMIZE screens (rebuilt 2026-10-18; owner: "This needs a AAA upgrade").
    /// Both stand in the v6.3 sanctuary of the main menu (room.png: the torch-lit wall between two pylons): the live 3D
    /// duelist on a gold-ringed plinth in the middle (<see cref="AvatarPreview3d"/>, one body shared by both screens),
    /// the left pylon holds the categories (customizer) or the duelist's identity (profile), the right pylon holds the
    /// wardrobe grid and its editors (customizer) or stats and the spirit companion (profile).
    /// The screens are opaque: the menu underneath no longer bleeds through. The camera follows the category (face for
    /// FACE SHAPE, feet for SHOES ...). Landscape (the 1920x1080 plan the owner judges in) and the portrait phone plan
    /// share the editors; only the frame differs. Saves to the local account and refreshes the map / hub portraits.
    /// </summary>
    public partial class AvatarCustomizerUI : MonoBehaviour
    {
        Transform _canvasRoot;
        CanvasScaler _scaler;
        bool _landscape = true;
        GameObject _profileSheet, _customizerSheet;
        AvatarPreview3d _preview3d;
        AvatarPortraitView _fallback2d;
        RectTransform _profileStage, _customizerStage;

        AvatarAppearance _draft;
        string _savedJson;
        ClothingWardrobe _wardrobe;
        Action _onSaved;

        enum Part { Look, Hat, Facial, Shirt, Hands, Bottoms, Shoes, Accent, Title, Skin, HairColor, OutfitTint, Disk, Aura, Body, Hair, Paint, Outfit, Neck, FaceShape, Accessories, Tattoos }
        enum Cat { Body, Face, Hair, Outfits, Wardrobe, Colours, Adorn, Duelist }

        Cat _cat = Cat.Body;
        Part _focus = Part.Body;
        string _hairZone = "root";
        string _paintKey = "Shirt.0";

        static readonly (Cat cat, string name, string caption, Part[] tabs)[] Cats =
        {
            (Cat.Body, "Body", "Base · height · build · skin", new[] { Part.Body, Part.Skin }),
            (Cat.Face, "Face", "Nose · mouth · jaw · brow", new[] { Part.FaceShape }),
            (Cat.Hair, "Hair", "Anime sculpts · styles · dye", new[] { Part.Hair, Part.HairColor }),
            (Cat.Outfits, "Outfits", "The cast's signature looks", new[] { Part.Outfit }),
            (Cat.Wardrobe, "Wardrobe", "Hats · tops · bottoms · shoes", new[] { Part.Hat, Part.Facial, Part.Neck, Part.Shirt, Part.Hands, Part.Bottoms, Part.Shoes }),
            (Cat.Colours, "Colours", "Paint the standard pieces", new[] { Part.Paint }),
            (Cat.Adorn, "Adornments", "Accessories · tattoos", new[] { Part.Accessories, Part.Tattoos }),
            (Cat.Duelist, "Duelist", "Title · spirit · disk · aura", new[] { Part.Title, Part.Accent, Part.Disk, Part.Aura, Part.Look }),
        };

        static string TabLabel(Part p) => p switch
        {
            Part.Body => "Build", Part.Skin => "Skin", Part.FaceShape => "Features", Part.Hair => "Style", Part.HairColor => "Dye",
            Part.Outfit => "Signature looks", Part.Hat => "Hats", Part.Facial => "Eyewear", Part.Neck => "Neck", Part.Shirt => "Tops",
            Part.Hands => "Gloves", Part.Bottoms => "Bottoms", Part.Shoes => "Shoes", Part.Paint => "Clothes", Part.Accessories => "Accessories",
            Part.Tattoos => "Tattoos", Part.Title => "Title", Part.Accent => "Spirit", Part.Disk => "Duel Disk", Part.Aura => "Aura",
            Part.Look => "Portrait", _ => p.ToString()
        };

        /// <summary>Where the camera looks while a tab is open.</summary>
        static AvatarPreview3d.Focus FocusFor(Part p) => p switch
        {
            Part.FaceShape or Part.Hat or Part.Facial => AvatarPreview3d.Focus.Face,
            Part.Hair or Part.HairColor or Part.Neck or Part.Tattoos or Part.Accessories => AvatarPreview3d.Focus.Bust,
            Part.Bottoms => AvatarPreview3d.Focus.Legs,
            Part.Shoes => AvatarPreview3d.Focus.Feet,
            _ => AvatarPreview3d.Focus.Full
        };

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
            var canvasGo = new GameObject("AvatarCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _landscape = UiTheme.ApplyForAspect(_scaler, MenuAspect.Surface);
            _canvasRoot = canvasGo.transform;
            BuildSheets();
            HideAllNow();
        }

        /// <summary>Rebuild both screens for the current aspect (also when the window flips between landscape and portrait).</summary>
        void BuildSheets()
        {
            if (_preview3d != null) _preview3d.transform.SetParent(_canvasRoot, false);   // keep the body while the sheets go
            if (_profileSheet != null) Destroy(_profileSheet);
            if (_customizerSheet != null) Destroy(_customizerSheet);
            BuildProfileSheet();
            BuildCustomizerSheet();
            if (_preview3d == null)
            {
                _preview3d = AvatarPreview3d.Create(_canvasRoot);
                _preview3d.gameObject.SetActive(false);
            }
        }

        bool _applyPending;
        float _nextApplyAt;

        void Update()
        {
            // slider drags: the body follows at most ~16 times a second (a sculpt re-follows the body's shapes on each apply)
            if (_applyPending && Time.unscaledTime >= _nextApplyAt)
            {
                _applyPending = false;
                _nextApplyAt = Time.unscaledTime + 0.06f;
                ApplyPreview();
            }
            var land = UiTheme.IsLandscapeSurface(MenuAspect.Surface);
            if (land == _landscape) return;
            var profileOpen = _profileSheet != null && _profileSheet.activeSelf;
            var customOpen = _customizerSheet != null && _customizerSheet.activeSelf;
            _landscape = UiTheme.ApplyForAspect(_scaler, MenuAspect.Surface);
            BuildSheets();
            HideAllNow();
            if (customOpen) ShowCustomizer(false);
            else if (profileOpen) ShowProfile(false);
        }

        // ------------------------------------------------------------------ open / close

        public void OpenProfile(Action onSaved = null)
        {
            StopAllCoroutines();
            _onSaved = onSaved;
            LoadDraftFromSession();
            ShowProfile(true);
        }

        public void OpenCustomizer(Action onSaved = null)
        {
            StopAllCoroutines();
            _onSaved = onSaved;
            StreamingSprite.ClearCache("WRLDZ/Avatar/clothes/");
            LoadDraftFromSession();
            ShowCustomizer(true);
        }

        void ShowProfile(bool animate)
        {
            HideAllNow();
            RefreshProfile();
            AttachPreview(_profileStage);
            ApplyPreview();
            _preview3d?.SetFocus(AvatarPreview3d.Focus.Full);
            _preview3d?.ResetView();
            SheetIn(_profileSheet, animate);
        }

        void ShowCustomizer(bool animate)
        {
            HideAllNow();
            if (_nameField != null)
            {
                var acc = AppSession.Ensure().Account;
                _nameField.SetTextWithoutNotify(acc?.displayName ?? "");
            }
            AttachPreview(_customizerStage);
            ApplyPreview();
            RebuildRail();
            SelectTab(_focus, true);
            SheetIn(_customizerSheet, animate);
        }

        public void HideAll()
        {
            CloseConfirm();
            foreach (var s in new[] { _profileSheet, _customizerSheet })
            {
                if (s == null) continue;
                if (s.activeSelf && isActiveAndEnabled) StartCoroutine(SheetOut(s));
                else s.SetActive(false);
            }
        }

        void HideAllNow()
        {
            CloseConfirm();
            if (_profileSheet != null) _profileSheet.SetActive(false);
            if (_customizerSheet != null) _customizerSheet.SetActive(false);
            if (_preview3d != null) _preview3d.gameObject.SetActive(false);
        }

        void SheetIn(GameObject sheet, bool animate)
        {
            if (sheet == null) return;
            sheet.SetActive(true);
            if (_preview3d != null) _preview3d.gameObject.SetActive(true);
            var g = MenuMotion.EnsureGroup(sheet);
            if (!animate || !isActiveAndEnabled) { g.alpha = 1f; g.blocksRaycasts = g.interactable = true; return; }
            StartCoroutine(MenuMotion.Fade(g, 0f, 1f, 0.28f));
        }

        IEnumerator SheetOut(GameObject sheet)
        {
            var g = MenuMotion.EnsureGroup(sheet);
            yield return MenuMotion.Fade(g, g.alpha, 0f, 0.18f);
            if (sheet != null) sheet.SetActive(false);
            var anyOpen = (_profileSheet != null && _profileSheet.activeSelf) || (_customizerSheet != null && _customizerSheet.activeSelf);
            if (!anyOpen && _preview3d != null) _preview3d.gameObject.SetActive(false);
        }

        void AttachPreview(RectTransform host)
        {
            if (_preview3d == null || host == null) return;
            _preview3d.gameObject.SetActive(true);
            _preview3d.AttachTo(host);
        }

        void LoadDraftFromSession()
        {
            AppSession.Ensure().RefreshFromStore();
            var acc = AppSession.Ensure().Account;
            _draft = AvatarAppearance.Default();
            if (acc != null)
            {
                if (acc.avatar != null)
                    _draft = acc.avatar.Clone();
                else
                    _draft.accentHex = string.IsNullOrEmpty(acc.avatarColor) ? "#3ECFFF" : acc.avatarColor;
            }

            _draft.ClampToCatalog();
            // the player is always the 3D body: a sprite id from an old signature equip would turn the map token 2D
            _draft.characterId = "";
            _draft.silhouette = false;
            _savedJson = JsonUtility.ToJson(_draft);
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

        bool Dirty => _draft != null && JsonUtility.ToJson(_draft) != _savedJson;

        // ------------------------------------------------------------------ the sanctuary frame shared by both screens

        /// <summary>
        /// An opaque sheet with the sanctuary behind it: the room art fitted to the stage plan (landscape: the whole room,
        /// pylons where the main menu has them; portrait: the torch-lit wall), the dark ceiling and the floor vignette.
        /// Returns the stage container (units = plan pixels).
        /// </summary>
        RectTransform Sanctuary(GameObject sheet)
        {
            var bg = CzKit.Img(sheet.transform, "Backdrop", UiFoundation.WhiteSprite(), WrldzAnimePalette.RoomBase, true);
            CzKit.Fill(bg.rectTransform);
            var stage = CzKit.Node(sheet.transform, "Stage");
            var fit = stage.gameObject.AddComponent<CzStageFit>();
            fit.Reference = _landscape ? new Vector2(1920f, 1080f) : new Vector2(1080f, 2340f);
            fit.FitNow();
            var room = CzKit.Room;
            if (room != null)
            {
                var r = CzKit.Img(stage, "Room", room, Color.white);
                if (_landscape) CzKit.Place(r, 0f, 0f, 1920f, 1080f);
                else CzKit.Place(r, -1540f, 0f, 2340f * 16f / 9f, 2340f);   // scaled to the phone's height, the wall's middle (x 960) at its centre
            }
            // the room's top is unpainted night: deepen it under the title, and pool the floor
            var top = CzKit.Img(stage, "CeilingShade", CzKit.VFade(), new Color(0f, 0f, 0f, 0.55f));
            top.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            CzKit.Place(top, 0f, 0f, fit.Reference.x, _landscape ? 150f : 260f);
            var floor = CzKit.Img(stage, "FloorShade", CzKit.VFade(), new Color(0f, 0f, 0f, 0.62f));
            CzKit.Place(floor, 0f, fit.Reference.y - (_landscape ? 150f : 260f), fit.Reference.x, _landscape ? 150f : 260f);
            // a torch-warm light pool behind the duelist (the room's own torch is at the left of the wall)
            var pool = CzKit.Img(stage, "LightPool", CzKit.Soft(), new Color(0.95f, 0.68f, 0.38f, 0.16f));
            if (_landscape) CzKit.Place(pool, 610f, 170f, 700f, 900f);
            else CzKit.Place(pool, 90f, 140f, 900f, 1120f);
            return stage;
        }

        /// <summary>The stage slot the 3D body is filmed into, with the 2D portrait as the fallback when the 3D base is missing.</summary>
        RectTransform StageSlot(RectTransform stage, float x, float y, float w, float h)
        {
            var slot = CzKit.Node(stage, "DuelistSlot");
            CzKit.Place(slot, x, y, w, h);
            return slot;
        }

        // ------------------------------------------------------------------ CUSTOMIZE

        RectTransform _railHost, _rightHost;
        Text _catTitle, _catCaption, _subTitle, _detailName, _detailBlurb, _detailMeta, _toast;
        InputField _nameField;
        Text _nameTitle;
        RectTransform _optionHost;          // the current tab's scroll content (editors build into it)
        ScrollRect _optionScroll;
        CzFlow _flow;
        readonly List<(Button b, AvatarPreview3d.Focus f)> _viewChips = new();
        GameObject _confirm;
        float _railY0;

        // plan rects (stage px), set per aspect in BuildCustomizerSheet
        Rect _rRight, _rDetail;
        int _cols;
        float _k;

        void BuildCustomizerSheet()
        {
            _customizerSheet = new GameObject("CustomizerSheet", typeof(RectTransform));
            _customizerSheet.transform.SetParent(_canvasRoot, false);
            CzKit.Fill((RectTransform)_customizerSheet.transform);
            var stage = Sanctuary(_customizerSheet);
            _k = _landscape ? 1f : 1.5f;
            _cols = _landscape ? 3 : 4;

            if (_landscape)
            {
                // top bar over the ceiling: BACK · title · RESET · SAVE (the buttons line up with the pylons' gold edges)
                CzKit.Btn(stage, "Back", 98f, 34f, 200f, 58f, "‹  BACK", CzKit.Kind.Plain, OnBack);
                var title = CzKit.Title(stage, "SPIRIT DUELER", 46);
                CzKit.Place(title, 560f, 18f, 800f, 66f);
                _subTitle = CzKit.Txt(stage, "Sub", "", CzKit.Caps, 18, CzKit.Muted, TextAnchor.MiddleCenter, 4f);
                CzKit.Place(_subTitle, 560f, 82f, 800f, 26f);
                CzKit.Btn(stage, "Reset", 1424f, 34f, 190f, 58f, "RESET", CzKit.Kind.Plain, ResetDraft, 22);
                CzKit.Btn(stage, "Save", 1631f, 34f, 190f, 58f, "SAVE LOOK", CzKit.Kind.Gold, Save, 24);

                // left pylon: the categories
                _railHost = CzKit.Node(stage, "Rail");
                CzKit.Place(_railHost, 112f, 228f, 401f, 640f);
                _railY0 = 0f;

                // centre: the duelist
                _customizerStage = StageSlot(stage, 538f, 124f, 844f, 870f);
                BuildStageControls(stage, 612f, 900f, 1308f, 900f, 56f, 1300f, 142f, 82f, 38f);
                BuildNameplate(stage, 700f, 990f, 520f, 1f);

                // right pylon: tabs, the grid and the detail strip
                _rRight = new Rect(1410f, 226f, 393f, 620f);
                _rDetail = new Rect(1402f, 852f, 409f, 106f);
            }
            else
            {
                CzKit.Btn(stage, "Back", 36f, 54f, 196f, 70f, "‹  BACK", CzKit.Kind.Plain, OnBack, 28);
                var title = CzKit.Title(stage, "SPIRIT DUELER", 46);
                CzKit.Place(title, 240f, 40f, 600f, 70f);
                _subTitle = CzKit.Txt(stage, "Sub", "", CzKit.Caps, 22, CzKit.Muted, TextAnchor.MiddleCenter, 4f);
                CzKit.Place(_subTitle, 240f, 108f, 600f, 32f);
                CzKit.Btn(stage, "Save", 848f, 54f, 196f, 70f, "SAVE", CzKit.Kind.Gold, Save, 28);
                CzKit.Btn(stage, "Reset", 848f, 128f, 196f, 44f, "RESET", CzKit.Kind.Ghost, ResetDraft, 22);

                _customizerStage = StageSlot(stage, 0f, 150f, 1080f, 1000f);
                BuildStageControls(stage, 84f, 1060f, 996f, 1060f, 80f, 940f, 190f, 116f, 54f);
                BuildNameplate(stage, 200f, 1150f, 680f, 1.3f);

                // the lower slate: categories (a sideways strip), tabs, grid, detail
                var slate = CzKit.Img(stage, "LowerSlate", UiFoundation.WhiteSprite(), new Color(0.07f, 0.065f, 0.085f, 0.97f), true);
                CzKit.Place(slate, 20f, 1252f, 1040f, 1068f);
                CzKit.GoldFrame(slate.transform, 2f, 2.2f);
                _railHost = CzKit.Node(stage, "Rail");
                CzKit.Place(_railHost, 44f, 1270f, 992f, 96f);
                _rRight = new Rect(44f, 1376f, 992f, 760f);
                _rDetail = new Rect(36f, 2146f, 1008f, 162f);
            }

            // the right-hand panel host (rebuilt per category) and the detail strip
            _rightHost = CzKit.Node(stage, "RightPanel");
            CzKit.Place(_rightHost, _rRight.x, _rRight.y, _rRight.width, _rRight.height);
            BuildDetailStrip(stage);

            // the 2D portrait only when the 3D base is missing from the build (a quiet well behind it)
            var well = CzKit.Img(_customizerStage, "FallbackWell", UiFoundation.WhiteSprite(), Color.clear);
            CzKit.Fill(well.rectTransform, 40f);
            HubChrome.PaintWell(well);
            well.gameObject.SetActive(false);
            _fallback2d = AvatarPortraitView.CreateFullBody(well.transform, 0.05f, 0.05f, 0.95f, 0.95f, hideBackground: true, badgeCrop: false);

            _toast = CzKit.Txt(stage, "Toast", "", CzKit.Display, _landscape ? 26 : 34, CzKit.GoldHi, TextAnchor.MiddleCenter, 3f);
            if (_landscape) CzKit.Place(_toast, 700f, 940f, 520f, 40f); else CzKit.Place(_toast, 200f, 1090f, 680f, 52f);
            TextFx.HardDrop(_toast, new Color(0f, 0f, 0f, 0.9f), new Vector2(0f, 2f));
            _toast.color = new Color(1f, 1f, 1f, 0f);
        }

        void BuildStageControls(RectTransform stage, float lx, float ly, float rx, float ry, float d, float chipX, float chipY, float chipW, float chipH)
        {
            CzKit.Round(stage, "TurnLeft", lx, ly, d, "‹", () => { FreeUiKit.PlaySelect(); _preview3d?.Turn(45f); });
            CzKit.Round(stage, "TurnRight", rx, ry, d, "›", () => { FreeUiKit.PlaySelect(); _preview3d?.Turn(-45f); });
            _viewChips.Clear();
            var views = new[] { (AvatarPreview3d.Focus.Full, "FULL"), (AvatarPreview3d.Focus.Bust, "BUST"), (AvatarPreview3d.Focus.Face, "FACE") };
            for (var i = 0; i < views.Length; i++)
            {
                var (f, label) = views[i];
                var chip = CzKit.Chip(stage, chipX, chipY + i * (chipH + 6f), chipH, label, false, () =>
                {
                    FreeUiKit.PlaySelect();
                    _preview3d?.SetFocus(f);
                    RefreshViewChips();
                }, Mathf.RoundToInt(chipH * 0.5f), out _);
                CzKit.Place((RectTransform)chip.transform, chipX, chipY + i * (chipH + 6f), chipW, chipH);
                _viewChips.Add((chip, f));
            }
        }

        void RefreshViewChips()
        {
            var cur = _preview3d != null ? _preview3d.CurrentFocus : AvatarPreview3d.Focus.Full;
            foreach (var (b, f) in _viewChips)
            {
                if (b == null) continue;
                var on = f == cur;
                var bg = b.GetComponent<Image>();
                if (bg != null) bg.color = on ? new Color(CzKit.Gold.r, CzKit.Gold.g, CzKit.Gold.b, 0.16f) : new Color(0f, 0f, 0f, 0.35f);
                var hv = b.GetComponent<CzHover>();
                if (hv != null) { hv.LabelRest = on ? CzKit.GoldHi : CzKit.Muted; hv.LabelHover = on ? CzKit.GoldHi : CzKit.Text; }
            }
        }

        /// <summary>The cartouche under the plinth: the duelist's name (tap to edit) and title.</summary>
        void BuildNameplate(RectTransform stage, float x, float y, float w, float k)
        {
            var go = new GameObject("NameField", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(stage, false);
            var img = go.GetComponent<Image>();
            CzKit.Place(img, x, y, w, 48f * k);
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0f, 0f, 0f, 0.001f);
            var text = CzKit.Txt(go.transform, "Text", "", CzKit.Display, Mathf.RoundToInt(34 * k), CzKit.Text, TextAnchor.MiddleCenter, 1.5f);
            CzKit.Fill(text.rectTransform);
            text.supportRichText = false;
            TextFx.HardDrop(text, new Color(0f, 0f, 0f, 0.85f), new Vector2(0f, 2.5f));
            var ph = CzKit.Txt(go.transform, "Placeholder", "Name your duelist", CzKit.Display, Mathf.RoundToInt(30 * k), new Color(CzKit.Muted.r, CzKit.Muted.g, CzKit.Muted.b, 0.7f), TextAnchor.MiddleCenter, 1.5f);
            CzKit.Fill(ph.rectTransform);
            _nameField = go.GetComponent<InputField>();
            _nameField.textComponent = text;
            _nameField.placeholder = ph;
            _nameField.characterLimit = 24;
            _nameField.caretColor = CzKit.GoldHi;
            _nameField.selectionColor = new Color(CzKit.Gold.r, CzKit.Gold.g, CzKit.Gold.b, 0.35f);
            CzKit.GoldRule(stage, x + w * 0.15f, y + 48f * k, w * 0.7f, 0.85f);
            _nameTitle = CzKit.Txt(stage, "NameTitle", "", CzKit.Caps, Mathf.RoundToInt(17 * k), CzKit.Label, TextAnchor.MiddleCenter, 4f * k);
            CzKit.Place(_nameTitle, x, y + 52f * k, w, 26f * k);
        }

        void BuildDetailStrip(RectTransform stage)
        {
            var d = _rDetail;
            var back = CzKit.Img(stage, "DetailBacking", UiFoundation.WhiteSprite(), CzKit.Pylon);
            CzKit.Place(back, d.x, d.y, d.width, d.height);
            CzKit.GoldRule(stage, d.x + 8f, d.y, d.width - 16f, 0.9f);
            var pad = 10f * _k;
            _detailName = CzKit.Txt(stage, "DetailName", "", CzKit.Display, Mathf.RoundToInt(24 * _k), CzKit.Text, TextAnchor.MiddleLeft, 1f);
            CzKit.Place(_detailName, d.x + pad, d.y + 8f * _k, d.width - pad * 2f, 32f * _k);
            _detailBlurb = CzKit.Txt(stage, "DetailBlurb", "", CzKit.Body(500), Mathf.RoundToInt(15 * _k), CzKit.Muted, TextAnchor.UpperLeft, 0f, true);
            CzKit.Place(_detailBlurb, d.x + pad, d.y + 40f * _k, d.width - pad * 2f, 40f * _k);
            _detailBlurb.verticalOverflow = VerticalWrapMode.Truncate;
            _detailMeta = CzKit.Txt(stage, "DetailMeta", "", CzKit.Caps, Mathf.RoundToInt(16 * _k), CzKit.Label, TextAnchor.MiddleLeft, 2.5f);
            CzKit.Place(_detailMeta, d.x + pad, d.y + d.height - 30f * _k, d.width - pad * 2f, 24f * _k);
        }

        // ---- the category rail (left pylon / portrait strip)

        void RebuildRail()
        {
            if (_railHost == null) return;
            CzKit.Clear(_railHost);
            if (_landscape)
            {
                var head = CzKit.Txt(_railHost, "Head", "THE WARDROBE", CzKit.Caps, 17, CzKit.Muted, TextAnchor.MiddleLeft, 4f);
                CzKit.Place(head, 6f, 0f, 380f, 26f);
                CzKit.GoldRule(_railHost, 0f, 30f, 401f, 0.7f);
                var y = 44f;
                foreach (var (cat, name, caption, _) in Cats)
                {
                    RailRow(cat, name, caption, y);
                    y += 72f;
                }
            }
            else
            {
                var sr = default(ScrollRect);
                var vp = CzKit.Img(_railHost, "Strip", UiFoundation.WhiteSprite(), new Color(0f, 0f, 0f, 0.001f), true);
                CzKit.Fill(vp.rectTransform);
                vp.gameObject.AddComponent<RectMask2D>();
                var content = CzKit.Node(vp.transform, "Content");
                content.anchorMin = new Vector2(0f, 0f); content.anchorMax = new Vector2(0f, 1f); content.pivot = new Vector2(0f, 0.5f);
                sr = vp.gameObject.AddComponent<ScrollRect>();
                sr.viewport = vp.rectTransform; sr.content = content; sr.horizontal = true; sr.vertical = false;
                sr.movementType = ScrollRect.MovementType.Elastic; sr.elasticity = 0.08f;
                var x = 0f;
                foreach (var (cat, name, _, _) in Cats)
                {
                    var c = cat;
                    var chip = CzKit.Chip(content, x, 6f, 84f, name, c == _cat, () => SelectCategory(c), 30, out var w);
                    x += w + 10f;
                }
                content.sizeDelta = new Vector2(x, 0f);
            }
        }

        void RailRow(Cat cat, string name, string caption, float y)
        {
            var on = cat == _cat;
            var go = new GameObject("Cat_" + cat, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_railHost, false);
            var bg = go.GetComponent<Image>();
            CzKit.Place(bg, 0f, y, 401f, 64f);
            bg.sprite = CzKit.Plate ?? UiFoundation.WhiteSprite();
            if (CzKit.Plate != null) bg.type = Image.Type.Sliced;
            bg.color = CzKit.Plate != null ? (on ? new Color(1f, 0.92f, 0.78f) : new Color(0.86f, 0.86f, 0.9f)) : new Color(0.12f, 0.11f, 0.14f);
            bg.raycastTarget = true;
            if (on)
            {
                var wash = CzKit.Img(go.transform, "Chosen", UiFoundation.WhiteSprite(), new Color(CzKit.Gold.r, CzKit.Gold.g, CzKit.Gold.b, 0.12f));
                CzKit.Fill(wash.rectTransform, 3f);
                var bar = CzKit.Img(go.transform, "Bar", UiFoundation.WhiteSprite(), CzKit.GoldHi);
                CzKit.Place(bar, 4f, 10f, 5f, 44f);
                CzKit.GoldFrame(go.transform, 2f, 2.8f);
            }
            var mark = CzKit.Img(go.transform, "Mark", CzKit.Diamond(), on ? CzKit.GoldHi : CzKit.GoldLo);
            CzKit.Place(mark, 20f, 24f, 14f, 14f);
            var t = CzKit.Txt(go.transform, "Name", name.ToUpperInvariant(), CzKit.Display, 25, on ? CzKit.GoldHi : CzKit.Text, TextAnchor.MiddleLeft, 2f);
            CzKit.Place(t, 46f, 6f, 330f, 32f);
            var c = CzKit.Txt(go.transform, "Caption", caption, CzKit.Body(500), 14, CzKit.Muted, TextAnchor.MiddleLeft);
            CzKit.Place(c, 46f, 36f, 340f, 22f);
            var chev = CzKit.Txt(go.transform, "Chevron", "›", CzKit.Body(700), 30, on ? CzKit.GoldHi : CzKit.GoldLo, TextAnchor.MiddleCenter);
            CzKit.Place(chev, 360f, 12f, 32f, 40f);
            var b = go.GetComponent<Button>();
            b.targetGraphic = bg; b.transition = Selectable.Transition.None;
            var cc = cat;
            b.onClick.AddListener(() => SelectCategory(cc));
            var hv = go.AddComponent<CzHover>();
            hv.HoverScale = 1.015f;
            hv.Label = t; hv.LabelRest = t.color; hv.LabelHover = on ? CzKit.GoldHi : Color.white;
        }

        void SelectCategory(Cat cat)
        {
            FreeUiKit.PlaySelect();
            var tabs = TabsOf(cat);
            var keep = cat == _cat ? _focus : tabs[0];
            _cat = cat;
            RebuildRail();
            SelectTab(Array.IndexOf(tabs, keep) >= 0 ? keep : tabs[0], false);
        }

        static Part[] TabsOf(Cat cat)
        {
            foreach (var c in Cats) if (c.cat == cat) return c.tabs;
            return Cats[0].tabs;
        }

        static Cat CatOf(Part p)
        {
            foreach (var c in Cats) if (Array.IndexOf(c.tabs, p) >= 0) return c.cat;
            return Cat.Body;
        }

        /// <summary>Open a tab: the camera eases to its focus, the right panel is rebuilt.</summary>
        void SelectTab(Part part, bool instant)
        {
            _focus = part;
            var cat = CatOf(part);
            if (cat != _cat) { _cat = cat; RebuildRail(); }
            _preview3d?.SetFocus(FocusFor(part), instant);
            RefreshViewChips();
            RebuildRight();
        }

        // ---- the right panel: category title, tabs, the tab's editor, then the detail strip

        void RebuildRight()
        {
            if (_rightHost == null || _draft == null) return;
            CzKit.Clear(_rightHost);
            var w = _rRight.width;
            var y = 0f;
            (Cat cat, string name, string caption, Part[] tabs) def = Cats[0];
            foreach (var c in Cats) if (c.cat == _cat) def = c;
            if (_landscape)
            {
                _catTitle = CzKit.Txt(_rightHost, "CatTitle", def.name.ToUpperInvariant(), CzKit.Display, 32, CzKit.GoldHi, TextAnchor.MiddleLeft, 2.5f);
                CzKit.Place(_catTitle, 0f, 0f, w, 42f);
                TextFx.HardDrop(_catTitle, new Color(0f, 0f, 0f, 0.85f), new Vector2(0f, 2f));
                _catCaption = CzKit.Txt(_rightHost, "CatCaption", def.caption, CzKit.Body(500), 15, CzKit.Muted, TextAnchor.MiddleLeft);
                CzKit.Place(_catCaption, 0f, 42f, w, 22f);
                y = 72f;
            }
            // tabs (wrap to more rows when they do not fit)
            if (def.tabs.Length > 1)
            {
                var th = 36f * _k; var tx = 0f; var size = Mathf.RoundToInt(19 * _k);
                foreach (var p in def.tabs)
                {
                    var pp = p;
                    var chip = CzKit.Chip(_rightHost, tx, y, th, TabLabel(p), p == _focus, () => { FreeUiKit.PlaySelect(); SelectTab(pp, false); }, size, out var cw);
                    if (tx > 0f && tx + cw > w)
                    {
                        tx = 0f; y += th + 6f * _k;
                        CzKit.Place((RectTransform)chip.transform, tx, y, cw, th);
                    }
                    tx += cw + 6f * _k;
                }
                y += th + 8f * _k;
            }
            CzKit.GoldRule(_rightHost, 0f, y, w, 0.6f);
            y += 12f * _k;
            var content = CzKit.Scroll(_rightHost, "Options", 0f, y, w, _rRight.height - y, out _optionScroll, new Color(CzKit.Pylon.r, CzKit.Pylon.g, CzKit.Pylon.b, 1f));
            _optionHost = content;
            _flow = new CzFlow(content, w - 6f, _k);
            BuildEditor();
            _flow.Finish();
            _optionScroll.verticalNormalizedPosition = 1f;
            UpdateLabels();
        }

        /// <summary>Redraw only the current tab's options (after a click that changes what is chosen), keeping the scroll.</summary>
        void RebuildOptions()
        {
            if (_optionHost == null) { RebuildRight(); return; }
            var keep = _optionScroll != null ? _optionScroll.verticalNormalizedPosition : 1f;
            CzKit.Clear(_optionHost);
            _flow = new CzFlow(_optionHost, _rRight.width - 6f, _k) { NoReveal = true };
            BuildEditor();
            _flow.Finish();
            Canvas.ForceUpdateCanvases();
            if (_optionScroll != null) _optionScroll.verticalNormalizedPosition = keep;
            UpdateLabels();
        }

        // ------------------------------------------------------------------ labels, preview, save

        void UpdateLabels()
        {
            if (_draft == null) return;
            if (_subTitle != null)
            {
                var catName = "";
                foreach (var c in Cats) if (c.cat == _cat) catName = c.name;
                _subTitle.text = ("CUSTOMIZE  ·  " + catName + "  ·  " + TabLabel(_focus)).ToUpperInvariant() + (Dirty ? "  ·  UNSAVED" : "");
            }
            if (_nameTitle != null) _nameTitle.text = (string.IsNullOrEmpty(_draft.title) ? "Spirit Dueler" : _draft.title).ToUpperInvariant();
            var (name, blurb, meta) = DetailFor(_focus);
            var pockets = ClothingService.PreviewPockets(_draft, _wardrobe);
            var decks = pockets == 1 ? "1 ON-HAND DECK" : pockets + " ON-HAND DECKS";
            if (_detailName != null) _detailName.text = name;
            if (_detailBlurb != null) _detailBlurb.text = blurb;
            if (_detailMeta != null) _detailMeta.text = string.IsNullOrEmpty(meta) ? decks : meta + "   ·   " + decks;
        }

        void ApplyPreview()
        {
            _draft?.ClampToCatalog();
            if (_draft != null) { _draft.characterId = ""; _draft.silhouette = false; }
            var has3d = _preview3d != null && _preview3d.Apply(_draft);
            if (_fallback2d != null)
            {
                _fallback2d.transform.parent.gameObject.SetActive(!has3d && _customizerSheet != null && _customizerSheet.activeSelf);
                if (!has3d) _fallback2d.Apply(_draft);
            }
            if (_profileFallback != null)
            {
                _profileFallback.gameObject.SetActive(!has3d);
                if (!has3d) _profileFallback.Apply(_draft);
            }
        }

        /// <summary>Every edit goes through here: refresh the body, the labels and (when the choice moved) the options.</summary>
        void Changed(bool rebuild)
        {
            if (rebuild) { _applyPending = false; ApplyPreview(); }
            else _applyPending = true;
            UpdateLabels();
            if (rebuild) RebuildOptions();
        }

        void ResetDraft()
        {
            FreeUiKit.PlayClick();
            if (string.IsNullOrEmpty(_savedJson)) return;
            _draft = JsonUtility.FromJson<AvatarAppearance>(_savedJson);
            _draft.ClampToCatalog();
            Changed(true);
            Toast("RESTORED THE SAVED LOOK");
        }

        void OnBack()
        {
            FreeUiKit.PlayClick();
            if (Dirty) { OpenConfirm(); return; }
            ShowProfile(true);
        }

        void Save()
        {
            FreeUiKit.PlayConfirm();
            var session = AppSession.Ensure();
            var acc = session.Account;
            if (acc == null)
            {
                Toast("NO ACCOUNT SESSION");
                return;
            }

            _draft.ClampToCatalog();
            _draft.characterId = "";
            if (_nameField != null && !string.IsNullOrWhiteSpace(_nameField.text))
                acc.displayName = _nameField.text.Trim();

            acc.avatar = _draft.Clone();
            acc.avatarColor = _draft.accentHex; // keep legacy field in sync
            ClothingService.EnsureWardrobe(acc);

            if (!LocalAccountStore.TrySaveAccount(acc, out var err))
            {
                Toast((err ?? "SAVE FAILED").ToUpperInvariant());
                return;
            }

            session.SetAccount(acc);
            WRLDZ.Presentation.ArInteraction.SpiritDuelerPalette.NotifyChanged();
            _savedJson = JsonUtility.ToJson(_draft);
            Toast("LOOK SAVED");
            UpdateLabels();
            _onSaved?.Invoke();
            RefreshProfile();
        }

        Coroutine _toastCo;

        void Toast(string s)
        {
            if (_toast == null || !isActiveAndEnabled) return;
            if (_toastCo != null) StopCoroutine(_toastCo);
            _toastCo = StartCoroutine(ToastCo(s));
        }

        IEnumerator ToastCo(string s)
        {
            _toast.text = s;
            var t = 0f;
            while (t < 2.2f)
            {
                t += Time.unscaledDeltaTime;
                var a = t < 0.2f ? t / 0.2f : t > 1.7f ? 1f - (t - 1.7f) / 0.5f : 1f;
                if (_toast == null) yield break;
                _toast.color = new Color(CzKit.GoldHi.r, CzKit.GoldHi.g, CzKit.GoldHi.b, Mathf.Clamp01(a));
                yield return null;
            }
            if (_toast != null) _toast.color = new Color(CzKit.GoldHi.r, CzKit.GoldHi.g, CzKit.GoldHi.b, 0f);
        }

        // ---- leaving with unsaved changes

        void OpenConfirm()
        {
            CloseConfirm();
            if (_customizerSheet == null) return;
            _confirm = new GameObject("ConfirmLeave", typeof(RectTransform), typeof(Image));
            _confirm.transform.SetParent(_customizerSheet.transform, false);
            var dim = _confirm.GetComponent<Image>();
            dim.sprite = UiFoundation.WhiteSprite(); dim.color = new Color(0.01f, 0.01f, 0.02f, 0.72f); dim.raycastTarget = true;
            CzKit.Fill(dim.rectTransform);
            var stage = CzKit.Node(_confirm.transform, "Stage");
            var fit = stage.gameObject.AddComponent<CzStageFit>();
            fit.Reference = _landscape ? new Vector2(1920f, 1080f) : new Vector2(1080f, 2340f);
            fit.FitNow();
            var cx = fit.Reference.x * 0.5f; var cy = fit.Reference.y * 0.5f;
            var k = _landscape ? 1f : 1.4f;
            var pw = 640f * k; var ph = 270f * k;
            var panel = CzKit.Slate(stage, "Panel", Color.white, true);
            CzKit.Place(panel, cx - pw * 0.5f, cy - ph * 0.5f, pw, ph);
            CzKit.GoldFrame(panel.transform, 3f, 2.4f);
            var t = CzKit.Txt(panel.transform, "Q", "LEAVE WITHOUT SAVING?", CzKit.Display, Mathf.RoundToInt(30 * k), CzKit.GoldHi, TextAnchor.MiddleCenter, 2f);
            CzKit.Place(t, 20f, 30f * k, pw - 40f, 44f * k);
            var s = CzKit.Txt(panel.transform, "Sub", "The changes to this look have not been saved.", CzKit.Body(500), Mathf.RoundToInt(18 * k), CzKit.Muted, TextAnchor.MiddleCenter);
            CzKit.Place(s, 20f, 80f * k, pw - 40f, 30f * k);
            var bw = (pw - 80f) / 3f; var by = ph - 88f * k;
            CzKit.Btn(panel.transform, "Keep", 20f, by, bw, 60f * k, "KEEP EDITING", CzKit.Kind.Plain, () => { FreeUiKit.PlayClick(); CloseConfirm(); }, Mathf.RoundToInt(19 * k));
            CzKit.Btn(panel.transform, "Discard", 40f + bw, by, bw, 60f * k, "DISCARD", CzKit.Kind.Danger, () =>
            {
                FreeUiKit.PlayClick();
                CloseConfirm();
                LoadDraftFromSession();
                ShowProfile(true);
            }, Mathf.RoundToInt(19 * k));
            CzKit.Btn(panel.transform, "SaveLeave", 60f + bw * 2f, by, bw, 60f * k, "SAVE", CzKit.Kind.Gold, () =>
            {
                CloseConfirm();
                Save();
                if (!Dirty) ShowProfile(true);
            }, Mathf.RoundToInt(20 * k));
            var g = MenuMotion.EnsureGroup(_confirm);
            StartCoroutine(MenuMotion.Fade(g, 0f, 1f, 0.18f));
        }

        void CloseConfirm()
        {
            if (_confirm != null) Destroy(_confirm);
            _confirm = null;
        }

        // ------------------------------------------------------------------ PROFILE

        Text _prName, _prTitle, _prHandle, _prLevel, _prXp, _prTeam, _prWallet0, _prWallet1, _prWallet2;
        Text _statDuels, _statCards, _statPath, _statSe;
        Image _prXpFill, _prTeamIcon;
        SoulBadgeView _soulBadge;
        RectTransform _companionHost;
        AvatarPortraitView _profileFallback;
        Text _prNameplate, _prNameTitle;

        void BuildProfileSheet()
        {
            _profileSheet = new GameObject("ProfileSheet", typeof(RectTransform));
            _profileSheet.transform.SetParent(_canvasRoot, false);
            CzKit.Fill((RectTransform)_profileSheet.transform);
            var stage = Sanctuary(_profileSheet);
            var k = _landscape ? 1f : 1.45f;
            RectTransform left, right;
            if (_landscape)
            {
                CzKit.Btn(stage, "Close", 98f, 34f, 200f, 58f, "‹  BACK", CzKit.Kind.Plain, () => { FreeUiKit.PlayClick(); HideAll(); });
                var title = CzKit.Title(stage, "DUELIST PROFILE", 46);
                CzKit.Place(title, 560f, 18f, 800f, 66f);
                _prHandle = CzKit.Txt(stage, "Handle", "", CzKit.Caps, 18, CzKit.Muted, TextAnchor.MiddleCenter, 4f);
                CzKit.Place(_prHandle, 560f, 82f, 800f, 26f);
                CzKit.Btn(stage, "Customize", 1571f, 34f, 250f, 58f, "CUSTOMIZE", CzKit.Kind.Gold, OpenCustomizerFromProfile, 24);
                _profileStage = StageSlot(stage, 538f, 124f, 844f, 870f);
                left = CzKit.Node(stage, "Identity"); CzKit.Place(left, 112f, 228f, 401f, 640f);
                right = CzKit.Node(stage, "Record"); CzKit.Place(right, 1410f, 228f, 393f, 720f);
                _prNameplate = CzKit.Txt(stage, "Nameplate", "", CzKit.Display, 34, CzKit.Text, TextAnchor.MiddleCenter, 1.5f);
                CzKit.Place(_prNameplate, 700f, 990f, 520f, 48f);
                CzKit.GoldRule(stage, 778f, 1038f, 364f, 0.85f);
                _prNameTitle = CzKit.Txt(stage, "NameTitle", "", CzKit.Caps, 17, CzKit.Label, TextAnchor.MiddleCenter, 4f);
                CzKit.Place(_prNameTitle, 700f, 1042f, 520f, 26f);
                CzKit.Round(stage, "TurnLeft", 612f, 900f, 56f, "‹", () => { FreeUiKit.PlaySelect(); _preview3d?.Turn(45f); });
                CzKit.Round(stage, "TurnRight", 1308f, 900f, 56f, "›", () => { FreeUiKit.PlaySelect(); _preview3d?.Turn(-45f); });
            }
            else
            {
                CzKit.Btn(stage, "Close", 36f, 54f, 196f, 70f, "‹  BACK", CzKit.Kind.Plain, () => { FreeUiKit.PlayClick(); HideAll(); }, 28);
                var title = CzKit.Title(stage, "PROFILE", 46);
                CzKit.Place(title, 240f, 40f, 600f, 70f);
                _prHandle = CzKit.Txt(stage, "Handle", "", CzKit.Caps, 22, CzKit.Muted, TextAnchor.MiddleCenter, 3f);
                CzKit.Place(_prHandle, 240f, 108f, 600f, 32f);
                _profileStage = StageSlot(stage, 0f, 150f, 1080f, 940f);
                _prNameplate = CzKit.Txt(stage, "Nameplate", "", CzKit.Display, 44, CzKit.Text, TextAnchor.MiddleCenter, 1.5f);
                CzKit.Place(_prNameplate, 160f, 1080f, 760f, 60f);
                _prNameTitle = CzKit.Txt(stage, "NameTitle", "", CzKit.Caps, 22, CzKit.Label, TextAnchor.MiddleCenter, 4f);
                CzKit.Place(_prNameTitle, 160f, 1142f, 760f, 32f);
                var slate = CzKit.Img(stage, "LowerSlate", UiFoundation.WhiteSprite(), new Color(0.07f, 0.065f, 0.085f, 0.97f), true);
                CzKit.Place(slate, 20f, 1188f, 1040f, 1128f);
                CzKit.GoldFrame(slate.transform, 2f, 2.2f);
                left = CzKit.Node(stage, "Identity"); CzKit.Place(left, 48f, 1210f, 480f, 900f);
                right = CzKit.Node(stage, "Record"); CzKit.Place(right, 556f, 1210f, 480f, 900f);
                CzKit.Btn(stage, "Customize", 140f, 2196f, 800f, 96f, "CUSTOMIZE LOOK", CzKit.Kind.Gold, OpenCustomizerFromProfile, 36);
            }

            var pfWell = CzKit.Node(_profileStage, "Fallback2d");
            CzKit.Fill(pfWell, 60f);
            _profileFallback = AvatarPortraitView.CreateFullBodyFill(pfWell, hideBackground: true, badgeCrop: false);
            _profileFallback.gameObject.SetActive(false);

            BuildIdentity(left, k);
            BuildRecord(right, k);
        }

        void OpenCustomizerFromProfile()
        {
            FreeUiKit.PlaySelect();
            StreamingSprite.ClearCache("WRLDZ/Avatar/clothes/");
            _savedJson = JsonUtility.ToJson(_draft);
            ShowCustomizer(true);
        }

        void BuildIdentity(RectTransform host, float k)
        {
            var w = host.sizeDelta.x;
            var y = 0f;
            _prName = CzKit.Txt(host, "Name", "", CzKit.Display, Mathf.RoundToInt(38 * k), CzKit.Text, TextAnchor.MiddleLeft, 1f);
            CzKit.Place(_prName, 4f, y, w - 8f, 50f * k);
            TextFx.HardDrop(_prName, new Color(0f, 0f, 0f, 0.85f), new Vector2(0f, 2f));
            y += 52f * k;
            _prTitle = CzKit.Txt(host, "Title", "", CzKit.Caps, Mathf.RoundToInt(19 * k), CzKit.Label, TextAnchor.MiddleLeft, 3.5f);
            CzKit.Place(_prTitle, 4f, y, w - 8f, 26f * k);
            y += 40f * k;

            // level medallion + experience
            var d = 96f * k;
            var medal = CzKit.Img(host, "LevelMedal", CzKit.Circle(), new Color(0.11f, 0.17f, 0.40f));
            CzKit.Place(medal, 4f, y, d, d);
            var ring = CzKit.Img(medal.transform, "Ring", CzKit.Ring(), CzKit.Gold);
            CzKit.Fill(ring.rectTransform, -2f);
            var lvCap = CzKit.Txt(medal.transform, "Cap", "LV", CzKit.Caps, Mathf.RoundToInt(15 * k), CzKit.Label, TextAnchor.MiddleCenter, 2f);
            CzKit.Place(lvCap, 0f, d * 0.16f, d, d * 0.2f);
            _prLevel = CzKit.Txt(medal.transform, "Level", "1", CzKit.Display, Mathf.RoundToInt(38 * k), CzKit.Text, TextAnchor.MiddleCenter);
            CzKit.Place(_prLevel, 0f, d * 0.30f, d, d * 0.5f);
            var xpCap = CzKit.Txt(host, "XpCap", "EXPERIENCE", CzKit.Caps, Mathf.RoundToInt(16 * k), CzKit.Muted, TextAnchor.MiddleLeft, 3f);
            CzKit.Place(xpCap, d + 22f, y + 6f * k, w - d - 26f, 24f * k);
            _prXp = CzKit.Txt(host, "Xp", "", CzKit.Body(600), Mathf.RoundToInt(19 * k), CzKit.Text, TextAnchor.MiddleLeft);
            CzKit.Place(_prXp, d + 22f, y + 32f * k, w - d - 26f, 28f * k);
            var track = CzKit.Img(host, "XpTrack", CzKit.Track, CzKit.Track != null ? Color.white : new Color(0.05f, 0.05f, 0.07f));
            CzKit.Place(track, d + 22f, y + 66f * k, w - d - 28f, 14f * k);
            _prXpFill = CzKit.Img(track.transform, "Fill", CzKit.HGrad(), CzKit.Gold);
            var frt = _prXpFill.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0.01f, 1f);
            frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);
            y += d + 22f * k;

            CzKit.GoldRule(host, 0f, y, w, 0.6f);
            y += 16f * k;
            var wh = CzKit.Txt(host, "WalletHead", "WALLET", CzKit.Caps, Mathf.RoundToInt(16 * k), CzKit.Muted, TextAnchor.MiddleLeft, 4f);
            CzKit.Place(wh, 4f, y, w, 24f * k);
            y += 30f * k;
            _prWallet0 = WalletRow(host, ImagineAssets.IconDigizeni(), "DIGIZENI", ref y, w, k);
            _prWallet1 = WalletRow(host, ImagineAssets.IconDuelCoin(), "DUEL COIN", ref y, w, k);
            _prWallet2 = WalletRow(host, ImagineAssets.IconSetEnergy(), "SET ENERGY", ref y, w, k);
            y += 8f * k;
            CzKit.GoldRule(host, 0f, y, w, 0.6f);
            y += 16f * k;
            var th = CzKit.Txt(host, "TeamHead", "KURIBOH TEAM", CzKit.Caps, Mathf.RoundToInt(16 * k), CzKit.Muted, TextAnchor.MiddleLeft, 4f);
            CzKit.Place(th, 4f, y, w, 24f * k);
            y += 30f * k;
            _prTeamIcon = CzKit.Img(host, "TeamIcon", null, Color.white);
            _prTeamIcon.preserveAspect = true;
            CzKit.Place(_prTeamIcon, 4f, y, 56f * k, 56f * k);
            _prTeam = CzKit.Txt(host, "Team", "", CzKit.Display, Mathf.RoundToInt(24 * k), CzKit.Text, TextAnchor.MiddleLeft, 1f);
            CzKit.Place(_prTeam, 72f * k, y, w - 160f * k, 56f * k);
            _soulBadge = SoulBadgeView.Create(host);
            CzKit.Place((RectTransform)_soulBadge.transform, w - 76f * k, y - 6f * k, 70f * k, 70f * k);
        }

        Text WalletRow(RectTransform host, Sprite icon, string caption, ref float y, float w, float k)
        {
            var ic = CzKit.Img(host, "Icon_" + caption, icon, Color.white);
            ic.preserveAspect = true;
            CzKit.Place(ic, 4f, y + 2f * k, 30f * k, 30f * k);
            var c = CzKit.Txt(host, "Cap_" + caption, caption, CzKit.Caps, Mathf.RoundToInt(17 * k), CzKit.Label, TextAnchor.MiddleLeft, 2.5f);
            CzKit.Place(c, 44f * k, y, w * 0.5f, 34f * k);
            var v = CzKit.Txt(host, "Val_" + caption, "0", CzKit.Body(700), Mathf.RoundToInt(21 * k), CzKit.Text, TextAnchor.MiddleRight);
            CzKit.Place(v, w * 0.4f, y, w * 0.6f - 4f, 34f * k);
            y += 40f * k;
            return v;
        }

        void BuildRecord(RectTransform host, float k)
        {
            var w = host.sizeDelta.x;
            var y = 0f;
            var sh = CzKit.Txt(host, "StatsHead", "DUELIST RECORD", CzKit.Caps, Mathf.RoundToInt(16 * k), CzKit.Muted, TextAnchor.MiddleLeft, 4f);
            CzKit.Place(sh, 4f, y, w, 24f * k);
            y += 32f * k;
            _statDuels = StatRow(host, ImagineAssets.IconDuel(), "DUELS", ref y, w, k);
            _statCards = StatRow(host, ImagineAssets.IconDeck(), "CARDS", ref y, w, k);
            _statPath = StatRow(host, ImagineAssets.IconCompass(), "KM WALKED", ref y, w, k);
            _statSe = StatRow(host, ImagineAssets.IconSetEnergy(), "SET ENERGY", ref y, w, k);
            y += 6f * k;
            CzKit.GoldRule(host, 0f, y, w, 0.6f);
            y += 16f * k;
            var ch = CzKit.Txt(host, "SpiritHead", "SPIRIT COMPANION", CzKit.Caps, Mathf.RoundToInt(16 * k), CzKit.Muted, TextAnchor.MiddleLeft, 4f);
            CzKit.Place(ch, 4f, y, w, 24f * k);
            y += 32f * k;
            _companionHost = CzKit.Node(host, "Companions");
            CzKit.Place(_companionHost, 0f, y, w, (_landscape ? 310f : 520f));
            _companionK = k;
        }

        float _companionK = 1f;

        Text StatRow(RectTransform host, Sprite icon, string caption, ref float y, float w, float k)
        {
            var row = CzKit.Slate(host, "Stat_" + caption, Color.white);
            CzKit.Place(row, 0f, y, w, 56f * k);
            var ic = CzKit.Img(row.transform, "Icon", icon, Color.white);
            ic.preserveAspect = true;
            CzKit.Place(ic, 12f * k, 10f * k, 36f * k, 36f * k);
            var c = CzKit.Txt(row.transform, "Cap", caption, CzKit.Caps, Mathf.RoundToInt(17 * k), CzKit.Label, TextAnchor.MiddleLeft, 2.5f);
            CzKit.Place(c, 60f * k, 0f, w * 0.5f, 56f * k);
            var v = CzKit.Txt(row.transform, "Val", "0", CzKit.Display, Mathf.RoundToInt(28 * k), CzKit.Text, TextAnchor.MiddleRight, 1f);
            CzKit.Place(v, w * 0.4f, 0f, w * 0.6f - 14f * k, 56f * k);
            y += 64f * k;
            return v;
        }

        void RefreshProfile()
        {
            RefreshCompanions();
            var acc = AppSession.Ensure().Account;
            var title = string.IsNullOrEmpty(_draft?.title) ? "Spirit Dueler" : _draft.title;
            if (acc == null)
            {
                if (_prName != null) _prName.text = "Not signed in";
                if (_prNameplate != null) _prNameplate.text = "Not signed in";
                if (_prHandle != null) _prHandle.text = "";
                if (_prTeamIcon != null) _prTeamIcon.enabled = false;
                return;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();
            var p = acc.progress;
            var lv = p != null ? Mathf.Max(1, p.level) : Mathf.Max(1, acc.spiritRank);
            var name = string.IsNullOrEmpty(acc.displayName) ? acc.username : acc.displayName;
            if (_prName != null) { _prName.text = name; TextFx.FitWidth(_prName, ((RectTransform)_prName.transform).sizeDelta.x, 18); }
            if (_prNameplate != null) _prNameplate.text = name;
            if (_prTitle != null) _prTitle.text = title.ToUpperInvariant();
            if (_prNameTitle != null) _prNameTitle.text = title.ToUpperInvariant();
            if (_prHandle != null)
                _prHandle.text = (string.IsNullOrEmpty(acc.username) ? title : "@" + acc.username + "  ·  " + title).ToUpperInvariant();
            var team = p != null ? p.Team : KuribohTeam.None;
            if (_prTeam != null) _prTeam.text = KuribohTeamInfo.DisplayName(team);
            if (_prTeamIcon != null)
            {
                var spr = ImagineAssets.IconTeam(team);
                _prTeamIcon.enabled = spr != null;
                if (spr != null) _prTeamIcon.sprite = spr;
            }
            if (_prWallet0 != null) _prWallet0.text = (p?.digizeni ?? 0).ToString("N0");
            if (_prWallet1 != null) _prWallet1.text = (p?.duelCoin ?? 0).ToString("N0");
            if (_prWallet2 != null) _prWallet2.text = (p?.setEnergy ?? 0).ToString("N0");
            _soulBadge?.Bind(p);
            if (_prLevel != null) _prLevel.text = lv.ToString();
            if (p != null && _prXpFill != null)
            {
                var t = Mathf.Clamp(p.XpProgress01(), 0.02f, 1f);
                _prXpFill.rectTransform.anchorMax = new Vector2(t, 1f);
            }
            if (_prXp != null && p != null)
            {
                var need = p.XpToNextLevel();
                _prXp.text = need <= 0 ? "MAX LEVEL" : $"{p.xp:N0} / {need:N0}";
            }
            if (_statDuels != null) _statDuels.text = acc.duelsCompleted.ToString("N0");
            if (_statCards != null) _statCards.text = acc.cardsCollected.ToString("N0");
            if (_statPath != null) _statPath.text = acc.pathKm < 10f ? acc.pathKm.ToString("0.0") : acc.pathKm.ToString("N0");
            if (_statSe != null) _statSe.text = (p != null ? p.setEnergy : 0).ToString("N0");
        }
    }
}
