using System;
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
    /// Customization v1 editors for the 3D body (AVATAR_3D_INTEGRATION.md): BODY (base, height, weight),
    /// HAIR (style + named anime / lore palettes), HAIR COLOR (pattern + per-zone colour), SKIN and
    /// CLOTHES COLOR (per garment region). The strip row holds the choices; the controls area below holds
    /// sliders or the colour picker (swatches + hue / saturation / value). Every change goes to the live
    /// 3D preview through <see cref="ApplyPreview"/>; slider drags only refresh the preview and the label,
    /// never rebuild the rows (a rebuild would drop the drag).
    /// </summary>
    public partial class AvatarCustomizerUI
    {
        static readonly Dictionary<string, Sprite> s_thumbs = new();

        static Sprite HairThumb(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (s_thumbs.TryGetValue(id, out var sp)) return sp;
            var tex = Resources.Load<Texture2D>(OverworldAvatar3d.HairRes + "thumb_" + id);
            sp = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) : null;
            s_thumbs[id] = sp;
            return sp;
        }

        string HairName(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                var def = AvatarCatalog.FindHair(OverworldAvatar3d.DefaultHairId(_draft != null && _draft.IsFemale));
                return def != null ? def.name + " (base)" : "Base";
            }
            var h = AvatarCatalog.FindHair(id);
            return h != null ? h.name + (string.IsNullOrEmpty(h.inspiredBy) ? "" : "  ·  " + h.inspiredBy) : id;
        }

        static string PaintLabel(string key)
        {
            var dot = key.IndexOf('.');
            var part = dot > 0 ? key.Substring(0, dot) : key;
            int.TryParse(dot > 0 ? key.Substring(dot + 1) : "0", out var region);
            foreach (var set in new[] { AvatarCatalog.PaintParts, AvatarCatalog.OutfitPaintParts })
                foreach (var pp in set)
                    if (pp.key == part)
                        return pp.label + " " + (region < pp.regions.Length ? pp.regions[region] : region.ToString());
            return key;
        }

        static Color Hex(string hex, Color fallback) => AvatarPortraitView.Parse(hex, fallback);

        void Changed(bool rebuild)
        {
            ApplyPreview();
            UpdateOptionLabel();
            if (rebuild) RebuildOptionStrip();
        }

        void BuildEditor3d()
        {
            switch (_focus)
            {
                case Part.Body: BuildBodyEditor(); break;
                case Part.Hair: BuildHairEditor(); break;
                case Part.HairColor: BuildHairColorEditor(); break;
                case Part.Skin: BuildSkinEditor(); break;
                case Part.Paint: BuildPaintEditor(); break;
                case Part.Outfit: BuildOutfitEditor(); break;
            }
        }

        // ---------------------------------------------------------------- BODY
        void BuildBodyEditor()
        {
            var baseIcon = ImagineAssets.IconSlotLook();
            OptionTile(_optionHost, baseIcon, !_draft.IsFemale, Color.white, () =>
            {
                if (_draft.IsFemale) { _draft.bodyType = 0; _draft.heightCm = 0f; }
                Changed(true);
            }, "MALE", 110f);
            OptionTile(_optionHost, baseIcon, _draft.IsFemale, new Color(1f, 0.82f, 0.92f), () =>
            {
                if (!_draft.IsFemale) { _draft.bodyType = 1; _draft.heightCm = 0f; }
                Changed(true);
            }, "FEMALE", 110f);

            var fem = _draft.IsFemale;
            var minH = AvatarCatalog.HeightMinCm(fem);
            var maxH = AvatarCatalog.HeightMaxCm(fem);
            SliderRow(_controlsHost, 0.55f, 0.95f, "HEIGHT", minH, maxH, _draft.EffectiveHeightCm, true,
                v => $"{v:0} cm  ·  {Feet(v)}",
                v => { _draft.heightCm = Mathf.Round(v); Changed(false); });
            SliderRow(_controlsHost, 0.05f, 0.45f, "WEIGHT", 0f, 100f, _draft.weight * 100f, true,
                v => $"{v:0}  ·  {WeightWord(v)}",
                v => { _draft.weight = Mathf.Round(v) / 100f; Changed(false); });
        }

        static string Feet(float cm)
        {
            var inches = Mathf.RoundToInt(cm / 2.54f);
            return $"{inches / 12}'{inches % 12}\"";
        }

        static string WeightWord(float v) => v < 20 ? "Slim" : v < 40 ? "Lean" : v <= 60 ? "Average" : v <= 80 ? "Sturdy" : "Heavy";

        // ---------------------------------------------------------------- HAIR (style + palettes)
        void BuildHairEditor()
        {
            var cur = _draft.hairStyleId ?? "";
            var defId = OverworldAvatar3d.DefaultHairId(_draft.IsFemale);
            OptionTile(_optionHost, HairThumb(defId) ?? ImagineAssets.IconSlotHat(), cur.Length == 0, Color.white, () =>
            {
                _draft.hairStyleId = "";
                Changed(true);
            }, "BASE", 96f);
            foreach (var h in AvatarCatalog.HairStyles)
            {
                if (h.id == defId) continue;
                var id = h.id;
                OptionTile(_optionHost, HairThumb(id) ?? ImagineAssets.IconSlotHat(), cur == id, Color.white, () =>
                {
                    _draft.hairStyleId = id;
                    // a style arrives in its own colours unless the player already chose a pattern
                    Changed(true);
                }, h.name, 96f);
            }

            // palettes: "the style's own colours" first, then every named anime / lore palette
            var strip = ControlsStrip(0.02f, 0.98f);
            OptionTile(strip, UiFoundation.WhiteSprite(), _draft.hairPattern == 0, new Color(0.3f, 0.32f, 0.38f), () =>
            {
                _draft.hairPattern = (int)HairPattern.Original;
                Changed(true);
            }, "STYLE COLORS", 104f);
            var style = AvatarCatalog.FindHair(string.IsNullOrEmpty(cur) ? defId : cur);
            if (style?.palette != null)
                PaletteTile(strip, style.palette, "CANON");
            foreach (var pal in AvatarCatalog.HairPalettes) PaletteTile(strip, pal, pal.name);
        }

        void PaletteTile(Transform host, AvatarCatalog.HairPaletteEntry pal, string label)
        {
            var go = OptionTile(host, UiFoundation.WhiteSprite(), false, Hex(pal.root, Color.gray), () =>
            {
                pal.ApplyTo(_draft);
                Changed(true);
            }, label, 104f);
            // stripes for the zones the pattern uses
            var zones = new List<string>();
            foreach (var z in new[] { "tip", "fringe", "streak", "side" })
                if (AvatarCatalog.PatternUses(pal.Pattern, z)) zones.Add(z);
            for (var i = 0; i < zones.Count; i++)
            {
                var hex = zones[i] switch { "tip" => pal.tip, "fringe" => pal.fringe, "streak" => pal.streak, _ => pal.side };
                var st = new GameObject("Zone", typeof(RectTransform), typeof(Image));
                st.transform.SetParent(go.transform, false);
                var x0 = 0.55f + i * 0.14f;
                GoTheme.Place(st.GetComponent<RectTransform>(), x0, 0.45f, x0 + 0.12f, 0.95f);
                var im = st.GetComponent<Image>(); im.sprite = UiFoundation.WhiteSprite(); im.color = Hex(hex, Color.gray); im.raycastTarget = false;
            }
        }

        // ---------------------------------------------------------------- HAIR COLOR (pattern + zones)
        static readonly (string zone, string label)[] HairZones =
        {
            ("root", "ROOT"), ("tip", "TIPS"), ("fringe", "BANGS"), ("streak", "STREAKS"), ("side", "SPLIT")
        };

        string ZoneHex(string zone) => zone switch
        {
            "tip" => _draft.hairTipHex, "fringe" => _draft.hairFringeHex, "streak" => _draft.hairStreakHex,
            "side" => _draft.hairSideHex, _ => _draft.hairHex
        };

        void SetZoneHex(string zone, string hex)
        {
            switch (zone)
            {
                case "tip": _draft.hairTipHex = hex; break;
                case "fringe": _draft.hairFringeHex = hex; break;
                case "streak": _draft.hairStreakHex = hex; break;
                case "side": _draft.hairSideHex = hex; break;
                default: _draft.hairHex = hex; break;
            }
        }

        void BuildHairColorEditor()
        {
            var pat = (HairPattern)_draft.hairPattern;
            for (var i = 0; i < AvatarCatalog.HairPatternNames.Length; i++)
            {
                var p = (HairPattern)i;
                OptionTile(_optionHost, ImagineAssets.IconSlotAccent(), p == pat, Color.white, () =>
                {
                    _draft.hairPattern = (int)p;
                    if (!AvatarCatalog.PatternUses(p, _hairZone)) _hairZone = "root";
                    Changed(true);
                }, AvatarCatalog.HairPatternNames[i].ToUpperInvariant(), 96f);
            }
            if (pat == HairPattern.Original)
            {
                Hint(_controlsHost, "Original shows the hairstyle's own painted colours. Pick SOLID, OMBRÉ TIPS, TRI-COLOR, STREAKS or SPLIT to recolour it, or choose a named palette under HAIR.");
                return;
            }
            // zone chips: the zones this pattern paints
            foreach (var (zone, label) in HairZones)
            {
                if (!AvatarCatalog.PatternUses(pat, zone)) continue;
                var z = zone;
                OptionTile(_optionHost, UiFoundation.WhiteSprite(), _hairZone == z, Hex(ZoneHex(z), Color.gray), () =>
                {
                    _hairZone = z;
                    Changed(true);
                }, label, 84f);
            }
            if (!AvatarCatalog.PatternUses(pat, _hairZone)) _hairZone = "root";
            ColorPicker(_controlsHost, AvatarCatalog.HairSwatches, () => ZoneHex(_hairZone), hex => SetZoneHex(_hairZone, hex), allowOriginal: false);
        }

        // ---------------------------------------------------------------- SKIN
        void BuildSkinEditor()
        {
            var cur = _draft.GetPaint("Body.0");
            OptionTile(_optionHost, ImagineAssets.IconSlotFacial(), string.IsNullOrEmpty(cur), Color.white, () =>
            {
                _draft.SetPaint("Body.0", "");
                Changed(true);
            }, "ORIGINAL", 96f);
            foreach (var hex in AvatarCatalog.SkinPresets)
            {
                var h = hex;
                OptionTile(_optionHost, UiFoundation.WhiteSprite(), string.Equals(cur, h, StringComparison.OrdinalIgnoreCase), Hex(h, Color.gray), () =>
                {
                    _draft.SetPaint("Body.0", h);
                    _draft.skinHex = h;
                    Changed(true);
                });
            }
            ColorPicker(_controlsHost, AvatarCatalog.SkinPresets, () => _draft.GetPaint("Body.0"),
                hex => { _draft.SetPaint("Body.0", hex); if (!string.IsNullOrEmpty(hex)) _draft.skinHex = hex; }, allowOriginal: true);
        }

        // ---------------------------------------------------------------- OUTFIT (signature slot sets)
        void BuildOutfitEditor()
        {
            var fem = _draft.IsFemale;
            OptionTile(_optionHost, ImagineAssets.IconSlotShirt(), WearingSignature() == null && string.IsNullOrEmpty(_draft.outfitId), Color.white, () =>
            {
                ClearSignatureSlots();
                Changed(true);
            }, "BASE CLOTHES", 96f);
            var any = false;
            foreach (var look in CharacterLooks.All)
            {
                if (!look.FitsBody(fem)) continue;
                any = true;
                var pick = look;
                OptionTile(_optionHost, ImagineAssets.IconSlotShirt(), CharacterLooks.Wearing(_draft, pick), Color.white, () =>
                {
                    EquipSignature(pick);
                    Changed(true);
                }, pick.Name, 96f);
            }
            Hint(_controlsHost, any
                ? "A signature look is hat, shirt, pants, and shoes on this body. Yugi and Kaiba keep the sculpted clothes. The other looks are solid-colour shells."
                : "No signature looks are built for this body yet.");
        }

        CharacterLook WearingSignature()
        {
            if (_draft == null) return null;
            for (var i = 0; i < CharacterLooks.All.Length; i++)
                if (CharacterLooks.Wearing(_draft, CharacterLooks.All[i])) return CharacterLooks.All[i];
            return null;
        }

        void ClearSignatureSlots()
        {
            _draft.outfitId = "";
            _draft.hatId = ClothingCatalog.DefaultHat;
            _draft.facialId = ClothingCatalog.DefaultFacial;
            _draft.shirtId = ClothingCatalog.DefaultShirt;
            _draft.handsId = ClothingCatalog.DefaultHands;
            _draft.bottomsId = ClothingCatalog.DefaultBottoms;
            _draft.shoesId = ClothingCatalog.DefaultShoes;
            if (_paintKey.StartsWith("Outfit")) _paintKey = "Shirt.0";
        }

        void EquipSignature(CharacterLook look)
        {
            look.ApplyTo(_draft);
            GrantPiece(look.HatId);
            GrantPiece(look.FacialId);
            GrantPiece(look.ShirtId);
            GrantPiece(look.HandsId);
            GrantPiece(look.BottomsId);
            GrantPiece(look.ShoesId);
            if (_paintKey.StartsWith("Outfit")) _paintKey = "Shirt.0";
        }

        void GrantPiece(string id)
        {
            if (string.IsNullOrEmpty(id) || _wardrobe == null) return;
            _wardrobe.Grant(id);
        }

        string OutfitOptionLabel()
        {
            if (!string.IsNullOrEmpty(_draft.outfitId))
            {
                var old = AvatarCatalog.FindOutfit(_draft.outfitId);
                if (old != null) return "Outfit: " + old.name;
            }
            var worn = WearingSignature();
            return worn != null ? "Outfit: " + worn.Name : "Outfit: Base clothes";
        }

        // ---------------------------------------------------------------- CLOTHES COLOR
        void BuildPaintEditor()
        {
            var outfit = AvatarCatalog.FindOutfit(_draft.outfitId);
            var parts = outfit != null && outfit.FitsBody(_draft.IsFemale) ? AvatarCatalog.OutfitPaintParts : AvatarCatalog.PaintParts;
            if (outfit != null && !_paintKey.StartsWith("Outfit") && !_paintKey.StartsWith("Body")) _paintKey = "Outfit.0";
            if (outfit == null && _paintKey.StartsWith("Outfit")) _paintKey = "Shirt.0";
            foreach (var pp in parts)
            {
                for (var r = 0; r < pp.regions.Length; r++)
                {
                    if (pp.key == "Body" && r == 0) continue;   // skin has its own tab
                    var key = pp.key + "." + r;
                    var hex = _draft.GetPaint(key);
                    var label = (pp.key == "Body" ? pp.label : pp.label + " " + pp.regions[r]).ToUpperInvariant();
                    OptionTile(_optionHost, UiFoundation.WhiteSprite(), _paintKey == key,
                        string.IsNullOrEmpty(hex) ? new Color(0.22f, 0.24f, 0.30f) : Hex(hex, Color.gray), () =>
                        {
                            _paintKey = key;
                            Changed(true);
                        }, label, 96f);
                }
            }
            ColorPicker(_controlsHost, AvatarCatalog.ClothingSwatches, () => _draft.GetPaint(_paintKey),
                hex => _draft.SetPaint(_paintKey, hex), allowOriginal: true);
        }

        // ---------------------------------------------------------------- spirit companion (profile sheet)

        /// <summary>
        /// The spirit companion column left of the portrait: players see their Kuriboh; the lab admin account
        /// (<see cref="LabAdminService.IsAdmin"/>) sees all six and taps one to switch (saved on the account, map refreshed).
        /// </summary>
        void RefreshCompanions()
        {
            if (_companionHost == null) return;
            FloatingPanel.DestroyChildrenNow(_companionHost);
            var acc = AppSession.Ensure().Account;
            if (acc?.progress == null) return;
            var admin = LabAdminService.IsAdmin(acc);
            var current = KuribohCompanion.CurrentId();
            var list = new List<SpiritCompanions.Def>();
            if (admin) list.AddRange(SpiritCompanions.All);
            else if (SpiritCompanions.Find(current) != null) list.Add(SpiritCompanions.Find(current));
            if (list.Count == 0) return;

            var head = FloatingPanel.Body(_companionHost, admin ? "SPIRIT · ADMIN" : "SPIRIT", 12);
            GoTheme.Place(head.rectTransform, 0f, 0.93f, 1f, 1f);
            StyleReadable(head, 12, DuelystUi.GoldHot, TextAnchor.MiddleCenter);
            head.resizeTextForBestFit = true; head.resizeTextMinSize = 8; head.resizeTextMaxSize = 13;

            var cols = list.Count > 1 ? 2 : 1;
            var rows = (list.Count + cols - 1) / cols;
            const float gap = 0.03f;
            var cellW = (1f - gap * (cols - 1)) / cols;
            var cellH = Mathf.Min(0.42f, (0.92f - gap * (rows - 1)) / rows);
            for (var i = 0; i < list.Count; i++)
            {
                var d = list[i];
                var c = i % cols; var r = i / cols;
                var x0 = c * (cellW + gap); var y1 = 0.92f - r * (cellH + gap);
                var tile = new GameObject("Spirit_" + d.Id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
                tile.transform.SetParent(_companionHost, false);
                GoTheme.Place(tile.GetComponent<RectTransform>(), x0, y1 - cellH, x0 + cellW, y1);
                var bg = tile.GetComponent<Image>();
                bg.sprite = UiFoundation.WhiteSprite();
                bg.color = new Color(0.04f, 0.06f, 0.10f, 0.55f);
                var ring = tile.GetComponent<Outline>();
                ring.effectColor = DuelystUi.Gold; ring.effectDistance = new Vector2(2f, -2f); ring.useGraphicAlpha = false;
                ring.enabled = d.Id == current;

                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(tile.transform, false);
                GoTheme.Place(icon.GetComponent<RectTransform>(), 0.06f, 0.24f, 0.94f, 0.98f);
                var ii = icon.GetComponent<Image>();
                ii.sprite = CompanionThumb(d.Id) ?? ImagineAssets.IconSoul();
                ii.preserveAspect = true; ii.raycastTarget = false;

                var lbl = FloatingPanel.Body(tile.transform, d.Name.ToUpperInvariant(), 10);
                GoTheme.Place(lbl.rectTransform, 0.02f, 0f, 0.98f, 0.24f);
                StyleReadable(lbl, 10, DuelystUi.TextCream, TextAnchor.MiddleCenter);
                lbl.resizeTextForBestFit = true; lbl.resizeTextMinSize = 6; lbl.resizeTextMaxSize = 11;
                lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
                lbl.raycastTarget = false;

                var btn = tile.GetComponent<Button>();
                btn.targetGraphic = bg;
                btn.interactable = admin;
                var id = d.Id;
                btn.onClick.AddListener(() =>
                {
                    FreeUiKit.PlaySelect();
                    acc.progress.companionId = id;
                    ProgressionService.Persist(acc);
                    RefreshCompanions();
                    _onSaved?.Invoke();   // the map re-attaches its companion
                });
            }
        }

        static readonly Dictionary<string, Sprite> s_companionThumbs = new();

        static Sprite CompanionThumb(string id)
        {
            if (s_companionThumbs.TryGetValue(id, out var sp)) return sp;
            var tex = Resources.Load<Texture2D>(SpiritCompanionModel.Res + "thumb_" + id);
            sp = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) : null;
            s_companionThumbs[id] = sp;
            return sp;
        }

        // ---------------------------------------------------------------- widgets
        Transform ControlsStrip(float y0, float y1)
        {
            var strip = new GameObject("Strip", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            strip.transform.SetParent(_controlsHost, false);
            GoTheme.Place(strip.GetComponent<RectTransform>(), 0f, y0, 1f, y1);
            return MakeScrollStrip(strip);
        }

        static void Hint(Transform host, string text)
        {
            var t = FloatingPanel.Body(host, text, 14);
            GoTheme.Place(t.rectTransform, 0.02f, 0.05f, 0.98f, 0.95f);
            StyleReadable(t, 14, DuelystUi.TextMuted, TextAnchor.MiddleCenter);
        }

        /// <summary>Swatch row + hue / saturation / value sliders editing one hex. allowOriginal adds a reset swatch (empty hex).</summary>
        void ColorPicker(Transform host, IReadOnlyList<string> swatches, Func<string> get, Action<string> set, bool allowOriginal)
        {
            var strip = ControlsStrip(0.60f, 1f);
            var cur = get() ?? "";
            if (allowOriginal)
                OptionTile(strip, UiFoundation.WhiteSprite(), string.IsNullOrEmpty(cur), new Color(0.22f, 0.24f, 0.30f), () =>
                {
                    set("");
                    Changed(true);
                }, "ORIGINAL", 84f);
            foreach (var hex in swatches)
            {
                var h = hex;
                OptionTile(strip, UiFoundation.WhiteSprite(), string.Equals(cur, h, StringComparison.OrdinalIgnoreCase), Hex(h, Color.gray), () =>
                {
                    set(h);
                    Changed(true);
                }, null, 56f);
            }

            var start = Hex(string.IsNullOrEmpty(cur) ? "#808080" : cur, Color.gray);
            Color.RGBToHSV(start, out var hh, out var ss, out var vv);
            Image satTrack = null, valTrack = null, well = null;
            void Push()
            {
                var c = Color.HSVToRGB(hh, ss, vv);
                set("#" + ColorUtility.ToHtmlStringRGB(c));
                if (well != null) well.color = c;
                PaintTrack(satTrack, Color.HSVToRGB(hh, 0f, vv), Color.HSVToRGB(hh, 1f, vv));
                PaintTrack(valTrack, Color.black, Color.HSVToRGB(hh, ss, 1f));
                Changed(false);
            }
            var hueTrack = SliderRow(host, 0.40f, 0.57f, "HUE", 0f, 1f, hh, false, null, v => { hh = v; Push(); }, gradient: true);
            satTrack = SliderRow(host, 0.21f, 0.38f, "SAT", 0f, 1f, ss, false, null, v => { ss = v; Push(); }, gradient: true);
            valTrack = SliderRow(host, 0.02f, 0.19f, "BRIGHT", 0f, 1f, vv, false, null, v => { vv = v; Push(); }, gradient: true);
            if (hueTrack != null) { hueTrack.sprite = HueSprite(); hueTrack.color = Color.white; }
            PaintTrack(satTrack, Color.HSVToRGB(hh, 0f, vv), Color.HSVToRGB(hh, 1f, vv));
            PaintTrack(valTrack, Color.black, Color.HSVToRGB(hh, ss, 1f));

            var wellGo = new GameObject("Current", typeof(RectTransform), typeof(Image), typeof(Outline));
            wellGo.transform.SetParent(host, false);
            GoTheme.Place(wellGo.GetComponent<RectTransform>(), 0.90f, 0.04f, 1f, 0.55f);
            well = wellGo.GetComponent<Image>();
            well.sprite = UiFoundation.WhiteSprite();
            well.color = string.IsNullOrEmpty(cur) ? new Color(0.22f, 0.24f, 0.30f) : start;
            var o = wellGo.GetComponent<Outline>(); o.effectColor = DuelystUi.Gold; o.effectDistance = new Vector2(1.5f, -1.5f);
        }

        static Sprite s_hue;
        static Sprite HueSprite()
        {
            if (s_hue != null) return s_hue;
            var t = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (var x = 0; x < 128; x++) t.SetPixel(x, 0, Color.HSVToRGB(x / 127f, 1f, 1f));
            t.Apply();
            s_hue = Sprite.Create(t, new Rect(0, 0, 128, 1), new Vector2(0.5f, 0.5f));
            return s_hue;
        }

        /// <summary>Two-stop gradient track: a 2x1 texture per track, rewritten in place while dragging.</summary>
        static void PaintTrack(Image track, Color a, Color b)
        {
            if (track == null) return;
            var tex = track.sprite != null ? track.sprite.texture : null;
            if (tex == null || tex.width != 2 || tex.name != "GradTrack")
            {
                tex = new Texture2D(2, 1, TextureFormat.RGBA32, false) { name = "GradTrack", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                track.sprite = Sprite.Create(tex, new Rect(0, 0, 2, 1), new Vector2(0.5f, 0.5f));
            }
            tex.SetPixel(0, 0, a); tex.SetPixel(1, 0, b); tex.Apply();
            track.color = Color.white;
        }

        /// <summary>
        /// Label + slider (+ value text) in rows [y0, y1] of <paramref name="host"/>. Returns the track Image so the
        /// caller can paint a gradient on it (gradient = no fill bar).
        /// </summary>
        Image SliderRow(Transform host, float y0, float y1, string label, float min, float max, float value, bool showValue,
            Func<float, string> fmt, Action<float> onChange, bool gradient = false)
        {
            var row = new GameObject("Row_" + label, typeof(RectTransform));
            row.transform.SetParent(host, false);
            GoTheme.Place(row.GetComponent<RectTransform>(), 0f, y0, gradient ? 0.88f : 1f, y1);

            var lbl = FloatingPanel.Body(row.transform, label, 13);
            GoTheme.Place(lbl.rectTransform, 0f, 0f, 0.16f, 1f);
            StyleReadable(lbl, 13, DuelystUi.GoldHot, TextAnchor.MiddleLeft);
            lbl.resizeTextForBestFit = true; lbl.resizeTextMinSize = 8; lbl.resizeTextMaxSize = 15;

            Text val = null;
            if (showValue)
            {
                val = FloatingPanel.Body(row.transform, "", 13);
                GoTheme.Place(val.rectTransform, 0.70f, 0f, 1f, 1f);
                StyleReadable(val, 13, DuelystUi.TextCream, TextAnchor.MiddleRight);
                val.resizeTextForBestFit = true; val.resizeTextMinSize = 8; val.resizeTextMaxSize = 15;
            }

            var sgo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sgo.transform.SetParent(row.transform, false);
            GoTheme.Place(sgo.GetComponent<RectTransform>(), 0.17f, 0.05f, showValue ? 0.68f : 1f, 0.95f);

            var trackGo = new GameObject("Track", typeof(RectTransform), typeof(Image));
            trackGo.transform.SetParent(sgo.transform, false);
            GoTheme.Place(trackGo.GetComponent<RectTransform>(), 0f, 0.30f, 1f, 0.70f);
            var track = trackGo.GetComponent<Image>();
            track.sprite = UiFoundation.WhiteSprite();
            track.color = new Color(0.10f, 0.12f, 0.18f, 0.9f);

            var slider = sgo.GetComponent<Slider>();
            if (!gradient)
            {
                var fillArea = new GameObject("Fill Area", typeof(RectTransform));
                fillArea.transform.SetParent(sgo.transform, false);
                GoTheme.Place(fillArea.GetComponent<RectTransform>(), 0f, 0.30f, 1f, 0.70f);
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(fillArea.transform, false);
                var frt = fill.GetComponent<RectTransform>(); frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0f, 1f); frt.offsetMin = frt.offsetMax = Vector2.zero;
                var fi = fill.GetComponent<Image>(); fi.sprite = UiFoundation.WhiteSprite(); fi.color = DuelystUi.Gold;
                slider.fillRect = frt;
            }

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sgo.transform, false);
            var hart = handleArea.GetComponent<RectTransform>(); hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one;
            hart.offsetMin = new Vector2(8f, 0f); hart.offsetMax = new Vector2(-8f, 0f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image), typeof(Outline));
            handle.transform.SetParent(handleArea.transform, false);
            var hrt = handle.GetComponent<RectTransform>(); hrt.anchorMin = new Vector2(0f, 0.08f); hrt.anchorMax = new Vector2(0f, 0.92f);
            hrt.sizeDelta = new Vector2(16f, 0f);
            var hi = handle.GetComponent<Image>(); hi.sprite = UiFoundation.WhiteSprite(); hi.color = DuelystUi.TextCream;
            var ho = handle.GetComponent<Outline>(); ho.effectColor = new Color(0f, 0f, 0f, 0.8f); ho.effectDistance = new Vector2(1.5f, -1.5f);

            slider.handleRect = hrt;
            slider.targetGraphic = hi;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
            if (val != null && fmt != null) val.text = fmt(slider.value);
            slider.onValueChanged.AddListener(v =>
            {
                if (val != null && fmt != null) val.text = fmt(v);
                onChange?.Invoke(v);
            });
            return track;
        }
    }

    /// <summary>Calls <see cref="Changed"/> when its RectTransform changes size (orientation / window resize).</summary>
    public sealed class RectChangeRelay : MonoBehaviour
    {
        public Action Changed;
        void OnRectTransformDimensionsChange() => Changed?.Invoke();
    }
}
