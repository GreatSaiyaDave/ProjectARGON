using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// The CUSTOMIZE editors (AVATAR_3D_INTEGRATION.md), one per tab, built top-down into the right panel's scroll list
    /// by <see cref="CzFlow"/>: card grids for anything with a picture (bodies, hairstyles, outfits, wardrobe pieces,
    /// portraits), sliders for height / weight / builds / face shapes, colour gems plus hue-saturation-brightness for
    /// paints. A click that changes the choice redraws the list in place (same scroll); a slider drag only refreshes
    /// the body and the labels, never the rows under the finger.
    /// Pictures are the in-engine renders under Resources/WRLDZ/Avatar3D/Thumbs/ (Assets/Editor/WRLDZ/WardrobeThumbs.cs)
    /// when they exist, else the older Blender thumbs next to the pieces.
    /// </summary>
    public partial class AvatarCustomizerUI
    {
        // ------------------------------------------------------------------ pictures

        const string ThumbRes = "WRLDZ/Avatar3D/Thumbs/";
        static readonly Dictionary<string, Sprite> s_thumbs = new();

        static Sprite Thumb(string res)
        {
            if (string.IsNullOrEmpty(res)) return null;
            if (s_thumbs.TryGetValue(res, out var sp)) return sp;
            var tex = Resources.Load<Texture2D>(res);
            sp = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            s_thumbs[res] = sp;
            return sp;
        }

        static string Sex(bool female) => female ? "female" : "male";

        static Sprite ItemThumb(string id, bool female) =>
            Thumb(ThumbRes + "item_" + id + "_" + Sex(female))
            ?? Thumb(OverworldAvatar3d.ClothRes + "thumb_" + id + "_" + Sex(female))
            ?? Thumb(OverworldAvatar3d.ClothRes + "thumb_" + id + "_" + Sex(!female));

        /// <summary>The sculpted (_3d) hairs' Blender thumbs came out empty, so those wait for the in-engine render.</summary>
        static Sprite HairThumb(string id, bool female) =>
            Thumb(ThumbRes + "hair_" + id + "_" + Sex(female))
            ?? (id != null && id.EndsWith("_3d") ? null : Thumb(OverworldAvatar3d.HairRes + "thumb_" + id));

        static Sprite LookThumb(CharacterLook look, bool female) =>
            Thumb(ThumbRes + "look_" + look.Id) ?? ItemThumb(look.ShirtId, female);

        static Color Hex(string hex, Color fallback) => AvatarPortraitView.Parse(hex, fallback);

        // ------------------------------------------------------------------ dispatch

        void BuildEditor()
        {
            switch (_focus)
            {
                case Part.Body: BuildBodyEditor(); break;
                case Part.Skin: BuildSkinEditor(); break;
                case Part.FaceShape: BuildFaceShapeEditor(); break;
                case Part.Eyes: BuildEyesEditor(); break;
                case Part.Brows: BuildBrowsEditor(); break;
                case Part.Nose: BuildNoseEditor(); break;
                case Part.Mouth: BuildMouthEditor(); break;
                case Part.Head: BuildMorphGroupEditor("head", "Head type", "Round, long, square or child-proportioned. They add up."); break;
                case Part.Ears: BuildMorphGroupEditor("ears", "Ears", "Large, small or pointed."); break;
                case Part.Hair: BuildHairEditor(); break;
                case Part.HairColor: BuildHairColorEditor(); break;
                case Part.Outfit: BuildOutfitEditor(); break;
                case Part.Hat: BuildWardrobeEditor(ClothingSlot.Hat); break;
                case Part.Facial: BuildWardrobeEditor(ClothingSlot.Facial); break;
                case Part.Neck: BuildWardrobeEditor(ClothingSlot.Neck); break;
                case Part.Shirt: BuildWardrobeEditor(ClothingSlot.Shirt); break;
                case Part.Hands: BuildWardrobeEditor(ClothingSlot.Hands); break;
                case Part.Bottoms: BuildWardrobeEditor(ClothingSlot.Bottoms); break;
                case Part.Shoes: BuildWardrobeEditor(ClothingSlot.Shoes); break;
                case Part.Paint: BuildPaintEditor(); break;
                case Part.Accessories: BuildAdornmentEditor(false); break;
                case Part.Tattoos: BuildAdornmentEditor(true); break;
                case Part.Title: BuildTitleEditor(); break;
                case Part.Accent: BuildSpiritEditor(); break;
                case Part.Disk: BuildPaletteEditor(true); break;
                case Part.Aura: BuildPaletteEditor(false); break;
                case Part.Look: BuildPortraitEditor(); break;
            }
        }

        // ------------------------------------------------------------------ flow helpers

        float CardH(float w) => Mathf.Round(w * 1.26f);
        float Gap => 10f * _k;

        void Cards(int count, Func<int, (Sprite pic, string label, bool on, Action click, string tag, Color? tint)> cell)
        {
            var f = _flow;
            f.Grid(count, _cols, CardH((f.Width - Gap * (_cols - 1)) / _cols), Gap, (i, x, y, w, h) =>
            {
                var c = cell(i);
                var b = CzKit.Card(f.Content, "Card", x, y, w, h, c.pic, c.label, c.on, () =>
                {
                    FreeUiKit.PlaySelect();
                    c.click?.Invoke();
                }, c.tint, c.tag, f.S(16));
                f.Reveal(b);
            });
        }

        Slider Row(string label, float min, float max, float value, Func<float, string> fmt, Action<float> set, bool whole = false)
        {
            var f = _flow;
            var s = CzKit.SliderRow(f.Content, 0f, f.Y, f.Width, label, min, max, value, fmt, set, f.S(19), whole);
            f.Reveal(s.transform.parent as RectTransform);
            f.Y += f.S(19) * 3.4f + f.S(6);
            return s;
        }

        void Gems(IReadOnlyList<string> hexes, Func<string> get, Action<string> set, bool allowOriginal)
        {
            var f = _flow;
            var d = f.S(46);
            var gap = f.S(12);
            var perRow = Mathf.Max(1, Mathf.FloorToInt((f.Width + gap) / (d + gap)));
            var cur = get() ?? "";
            var items = new List<string>();
            if (allowOriginal) items.Add("");
            items.AddRange(hexes);
            for (var i = 0; i < items.Count; i++)
            {
                var hex = items[i];
                var x = (i % perRow) * (d + gap);
                var y = f.Y + (i / perRow) * (d + gap);
                var on = string.Equals(cur, hex, StringComparison.OrdinalIgnoreCase);
                var col = hex.Length == 0 ? new Color(0.16f, 0.16f, 0.2f) : Hex(hex, Color.gray);
                var h = hex;
                var b = CzKit.Swatch(f.Content, x, y, d, col, on, () =>
                {
                    FreeUiKit.PlaySelect();
                    set(h);
                    Changed(true);
                }, hex.Length == 0 ? "AUTO" : null);
                f.Reveal(b);
            }
            var rows = (items.Count + perRow - 1) / perRow;
            f.Y += rows * (d + gap) + f.S(4);
        }

        /// <summary>Hue / saturation / brightness sliders tuning one hex (a drag only repaints; the gems update on release of the tab).</summary>
        void FineTune(Func<string> get, Action<string> set)
        {
            var cur = get() ?? "";
            var start = Hex(string.IsNullOrEmpty(cur) ? "#808080" : cur, Color.gray);
            Color.RGBToHSV(start, out var hh, out var ss, out var vv);
            _flow.Header("Fine tune", string.IsNullOrEmpty(cur) ? "original" : cur.ToUpperInvariant());
            void Push()
            {
                set("#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(hh, ss, vv)));
                Changed(false);
            }
            Row("Hue", 0f, 360f, hh * 360f, v => $"{v:0}°", v => { hh = v / 360f; Push(); }, true);
            Row("Saturation", 0f, 100f, ss * 100f, v => $"{v:0}%", v => { ss = v / 100f; Push(); }, true);
            Row("Brightness", 0f, 100f, vv * 100f, v => $"{v:0}%", v => { vv = v / 100f; Push(); }, true);
        }

        void ListRows(int count, Func<int, (string label, Color? gem, bool on, Action click)> row)
        {
            var f = _flow;
            var h = f.S(54);
            for (var i = 0; i < count; i++)
            {
                var r = row(i);
                var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(f.Content, false);
                var bg = go.GetComponent<Image>();
                CzKit.Place(bg, 0f, f.Y, f.Width, h);
                bg.sprite = CzKit.Plate ?? UiFoundation.WhiteSprite();
                if (CzKit.Plate != null) bg.type = Image.Type.Sliced;
                bg.color = CzKit.Plate != null ? (r.on ? new Color(1f, 0.92f, 0.78f) : Color.white) : new Color(0.12f, 0.11f, 0.14f);
                bg.raycastTarget = true;
                var x0 = f.S(16);
                if (r.gem.HasValue)
                {
                    var ink = CzKit.Img(go.transform, "Ink", CzKit.Circle(), Color.black);
                    CzKit.Place(ink, x0, h * 0.5f - f.S(15), f.S(30), f.S(30));
                    var gem = CzKit.Img(ink.transform, "Gem", CzKit.Circle(), r.gem.Value);
                    CzKit.Fill(gem.rectTransform, f.S(3));
                    x0 += f.S(42);
                }
                var t = CzKit.Txt(go.transform, "Label", r.label, CzKit.Body(600), f.S(18), r.on ? CzKit.GoldHi : CzKit.Text, TextAnchor.MiddleLeft);
                CzKit.Place(t, x0, 0f, f.Width - x0 - f.S(40), h);
                if (r.on)
                {
                    CzKit.GoldFrame(go.transform, 2f, 2.8f);
                    var m = CzKit.Img(go.transform, "Mark", CzKit.Diamond(), CzKit.GoldHi);
                    CzKit.Place(m, f.Width - f.S(34), h * 0.5f - f.S(8), f.S(16), f.S(16));
                }
                var b = go.GetComponent<Button>();
                b.targetGraphic = bg; b.transition = Selectable.Transition.None;
                var click = r.click;
                b.onClick.AddListener(() => { FreeUiKit.PlaySelect(); click?.Invoke(); });
                var hv = go.AddComponent<CzHover>();
                hv.HoverScale = 1.012f; hv.Label = t; hv.LabelRest = t.color; hv.LabelHover = r.on ? CzKit.GoldHi : Color.white;
                f.Reveal(bg);
                f.Y += h + f.S(8);
            }
        }

        // ------------------------------------------------------------------ BODY

        static string Feet(float cm)
        {
            var inches = Mathf.RoundToInt(cm / 2.54f);
            return $"{inches / 12}'{inches % 12}\"";
        }

        static string WeightWord(float v) => v < 20 ? "Slim" : v < 40 ? "Lean" : v <= 60 ? "Average" : v <= 80 ? "Sturdy" : "Heavy";

        void BuildBodyEditor()
        {
            var f = _flow;
            f.Header("Base body");
            var fem = _draft.IsFemale;
            var w = (f.Width - Gap) * 0.5f;
            f.Grid(2, 2, Mathf.Round(w * 1.12f), Gap, (i, x, y, cw, ch) =>
            {
                var female = i == 1;
                var b = CzKit.Card(f.Content, female ? "Female" : "Male", x, y, cw, ch,
                    Thumb(ThumbRes + "body_" + Sex(female)) ?? ImagineAssets.IconSlotLook(), female ? "Female" : "Male", fem == female, () =>
                    {
                        FreeUiKit.PlaySelect();
                        if (_draft.IsFemale == female) return;
                        _draft.bodyType = female ? 1 : 0;
                        _draft.heightCm = 0f;
                        Changed(true);
                    }, null, null, f.S(18));
                f.Reveal(b);
            });
            f.Header("Stature");
            Row("Height", AvatarCatalog.HeightMinCm(fem), AvatarCatalog.HeightMaxCm(fem), _draft.EffectiveHeightCm,
                v => $"{v:0} cm  ·  {Feet(v)}", v => { _draft.heightCm = Mathf.Round(v); Changed(false); }, true);
            Row("Weight", 0f, 100f, _draft.weight * 100f, v => $"{v:0}  ·  {WeightWord(v)}",
                v => { _draft.weight = Mathf.Round(v) / 100f; Changed(false); }, true);
            f.Header("Build");
            foreach (var (key, label, group) in AvatarCatalog.BodyMorphs)
            {
                if (group != "build") continue;
                var k = key;
                Row(label, 0f, 100f, _draft.GetMorph(k) * 100f, v => $"{v:0}%", v => { _draft.SetMorph(k, v / 100f); Changed(false); }, true);
            }
            f.Hint("From 3 ft (Bonz) to 6 ft 8. Builds stack on top of weight; the clothes and sculpts follow the body.");
        }

        void BuildSkinEditor()
        {
            _flow.Header("Skin tone");
            Gems(AvatarCatalog.SkinPresets, () => _draft.GetPaint("Body.0"), hex =>
            {
                _draft.SetPaint("Body.0", hex);
                if (!string.IsNullOrEmpty(hex)) _draft.skinHex = hex;
            }, true);
            FineTune(() => _draft.GetPaint("Body.0"), hex => { _draft.SetPaint("Body.0", hex); _draft.skinHex = hex; });
        }

        // ------------------------------------------------------------------ FACE: eyes, brows, nose, mouth (FaceCatalog, drawn live by the face shader)

        static Shader s_faceShader;
        static Shader FaceShader() => s_faceShader ??= Shader.Find("WRLDZ/AvatarAnimeFace");

        /// <summary>A card whose picture is the feature itself, drawn by WRLDZ/AvatarAnimeFace at the card's size.</summary>
        void FeatureCard(float x, float y, float w, float h, string label, bool on, Action click, Action<Material> setup)
        {
            var f = _flow;
            var b = CzKit.Card(f.Content, "Card", x, y, w, h, null, label, on, () => { FreeUiKit.PlaySelect(); click?.Invoke(); }, null, null, f.S(16));
            var well = b.transform.Find("Well");
            var sh = FaceShader();
            if (well != null && sh != null)
            {
                var go = new GameObject("Feature", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(well, false);
                CzKit.Fill((RectTransform)go.transform, 2f);
                var ri = go.GetComponent<RawImage>();
                var mat = new Material(sh);
                var skin = Hex(_draft.GetPaint("Body.0"), new Color(0.98f, 0.77f, 0.58f));
                if (string.IsNullOrEmpty(_draft.GetPaint("Body.0"))) skin = _draft.IsFemale ? new Color(0.98f, 0.75f, 0.59f) : new Color(0.98f, 0.77f, 0.58f);
                mat.SetColor("_Skin", skin);
                setup(mat);
                ri.material = mat;
                ri.color = Color.white;
                ri.raycastTarget = false;
                _cardMats.Add(mat);
                // a skin backing so the feature reads on the dark well
                var bg = CzKit.Img(well, "SkinBacking", UiFoundation.WhiteSprite(), skin);
                CzKit.Fill(bg.rectTransform, 2f);
                bg.transform.SetAsFirstSibling();
            }
            f.Reveal(b);
        }

        readonly List<Material> _cardMats = new();

        void ReleaseCardMats()
        {
            foreach (var m in _cardMats) if (m != null) Destroy(m);
            _cardMats.Clear();
        }

        void SetIris(Material m, string hex)
        {
            var iris = Hex(string.IsNullOrEmpty(hex) ? FaceCatalog.DefaultIris : hex, new Color(0.36f, 0.24f, 0.62f));
            Color.RGBToHSV(iris, out var hh, out var ss, out var vv);
            m.SetColor("_IrisColor", iris);
            m.SetColor("_IrisLight", Color.HSVToRGB(hh, Mathf.Clamp01(ss * 0.75f), Mathf.Clamp01(vv * 1.25f + 0.12f)));
            m.SetColor("_IrisDark", Color.HSVToRGB(hh, Mathf.Clamp01(ss * 1.1f), vv * 0.45f));
        }

        void BuildEyesEditor()
        {
            var f = _flow;
            var cur = string.IsNullOrEmpty(_draft.eyeId) ? FaceCatalog.DefaultEye : _draft.eyeId;
            f.Header("Eye shape", FaceCatalog.Eyes.Length.ToString());
            var cw = (f.Width - Gap * (_cols - 1)) / _cols;
            f.Grid(FaceCatalog.Eyes.Length, _cols, Mathf.Round(cw * 0.95f), Gap, (i, x, y, w, h) =>
            {
                var e = FaceCatalog.Eyes[i];
                FeatureCard(x, y, w, h, e.Name, e.Id == cur, () => { _draft.eyeId = e.Id; Changed(true); }, m =>
                {
                    m.SetFloat("_Part", 0); m.SetFloat("_Mirror", 1);
                    m.SetFloat("_EyeW", e.W); m.SetFloat("_EyeH", e.H); m.SetFloat("_Tilt", e.Tilt); m.SetFloat("_Round", e.Round);
                    m.SetFloat("_TopBulge", e.TopBulge); m.SetFloat("_BotBulge", e.BotBulge); m.SetFloat("_Lash", e.Lash); m.SetFloat("_LashOuter", e.LashOuter);
                    m.SetFloat("_LowerLash", e.LowerLash); m.SetFloat("_Iris", e.Iris); m.SetFloat("_IrisScaleY", e.IrisScaleY); m.SetFloat("_Pupil", e.Pupil);
                    m.SetFloat("_Lid", e.RestLid);
                    SetIris(m, _draft.irisHex);
                });
            });
            f.Header("Iris colour");
            var hexes = new List<string>();
            foreach (var (_, hex) in FaceCatalog.IrisColours) hexes.Add(hex);
            Gems(hexes, () => string.IsNullOrEmpty(_draft.irisHex) ? FaceCatalog.DefaultIris : _draft.irisHex, hex => _draft.irisHex = hex, false);
            FineTune(() => string.IsNullOrEmpty(_draft.irisHex) ? FaceCatalog.DefaultIris : _draft.irisHex, hex => _draft.irisHex = hex);
        }

        void BuildBrowsEditor()
        {
            var f = _flow;
            var cur = string.IsNullOrEmpty(_draft.browId) ? FaceCatalog.DefaultBrow : _draft.browId;
            f.Header("Brow shape", FaceCatalog.Brows.Length.ToString());
            var cw = (f.Width - Gap * (_cols - 1)) / _cols;
            var browCol = Hex(string.IsNullOrEmpty(_draft.browHex) ? _draft.hairHex : _draft.browHex, new Color(0.16f, 0.1f, 0.08f));
            f.Grid(FaceCatalog.Brows.Length, _cols, Mathf.Round(cw * 0.8f), Gap, (i, x, y, w, h) =>
            {
                var e = FaceCatalog.Brows[i];
                FeatureCard(x, y, w, h, e.Name, e.Id == cur, () => { _draft.browId = e.Id; Changed(true); }, m =>
                {
                    m.SetFloat("_Part", 3); m.SetFloat("_Mirror", 1);
                    m.SetFloat("_BrowThick", e.Thick); m.SetFloat("_BrowArch", e.Arch); m.SetFloat("_BrowTilt", e.Tilt);
                    m.SetColor("_BrowColor", browCol);
                });
            });
            f.Header("Brow colour", string.IsNullOrEmpty(_draft.browHex) ? "follows the hair" : _draft.browHex.ToUpperInvariant());
            Gems(AvatarCatalog.HairSwatches, () => _draft.browHex, hex => _draft.browHex = hex, true);
        }

        void BuildNoseEditor()
        {
            var f = _flow;
            var cur = string.IsNullOrEmpty(_draft.noseId) ? FaceCatalog.DefaultNose : _draft.noseId;
            f.Header("Nose", FaceCatalog.Noses.Length.ToString());
            var cw = (f.Width - Gap * (_cols - 1)) / _cols;
            f.Grid(FaceCatalog.Noses.Length, _cols, Mathf.Round(cw * 0.95f), Gap, (i, x, y, w, h) =>
            {
                var e = FaceCatalog.Noses[i];
                FeatureCard(x, y, w, h, e.Name, e.Id == cur, () => { _draft.noseId = e.Id; Changed(true); }, m =>
                {
                    m.SetFloat("_Part", 2); m.SetFloat("_NoseStyle", e.Style); m.SetFloat("_NoseSize", e.Size * 1.6f); m.SetFloat("_NoseShade", Mathf.Max(e.Shade, 0.001f));
                });
            });
            f.Header("Nose shape");
            foreach (var (key, label, group) in AvatarCatalog.BodyMorphs)
            {
                if (group != "face" || !key.StartsWith("Face_Nose")) continue;
                var k = key;
                Row(label, 0f, 100f, _draft.GetMorph(k) * 100f, v => $"{v:0}%", v => { _draft.SetMorph(k, v / 100f); Changed(false); }, true);
            }
        }

        void BuildMouthEditor()
        {
            var f = _flow;
            var cur = string.IsNullOrEmpty(_draft.mouthId) ? FaceCatalog.DefaultMouth : _draft.mouthId;
            f.Header("Mouth", FaceCatalog.Mouths.Length.ToString());
            var cw = (f.Width - Gap * (_cols - 1)) / _cols;
            f.Grid(FaceCatalog.Mouths.Length, _cols, Mathf.Round(cw * 0.8f), Gap, (i, x, y, w, h) =>
            {
                var e = FaceCatalog.Mouths[i];
                FeatureCard(x, y, w, h, e.Name, e.Id == cur, () => { _draft.mouthId = e.Id; Changed(true); }, m =>
                {
                    m.SetFloat("_Part", 1); m.SetFloat("_MouthW", e.W * 1.2f); m.SetFloat("_LipLine", e.LipLine * 1.3f); m.SetFloat("_LowerLip", e.LowerLip);
                    m.SetFloat("_Corner", e.RestCorner); m.SetFloat("_Open", 0f);
                });
            });
            f.Header("Mouth shape");
            foreach (var (key, label, group) in AvatarCatalog.BodyMorphs)
            {
                if (group != "face" || !(key.StartsWith("Face_Mouth") || key.StartsWith("Face_Lips"))) continue;
                var k = key;
                Row(label, 0f, 100f, _draft.GetMorph(k) * 100f, v => $"{v:0}%", v => { _draft.SetMorph(k, v / 100f); Changed(false); }, true);
            }
            f.Hint("The mouth speaks and smiles on its own: it opens for words, lifts for a win, sets for an attack.");
        }

        /// <summary>Sliders for one morph group (head types, ears).</summary>
        void BuildMorphGroupEditor(string group, string title, string hint)
        {
            var f = _flow;
            f.Header(title);
            var any = false;
            foreach (var (key, label, g) in AvatarCatalog.BodyMorphs)
            {
                if (g != group) continue;
                any = true;
                var k = key;
                Row(label, 0f, 100f, _draft.GetMorph(k) * 100f, v => $"{v:0}%", v => { _draft.SetMorph(k, v / 100f); Changed(false); }, true);
            }
            if (!any) f.Hint("These shapes are not installed on the bodies yet.");
            else f.Hint(hint);
        }

        void BuildFaceShapeEditor()
        {
            _flow.Header("Features");
            foreach (var (key, label, group) in AvatarCatalog.BodyMorphs)
            {
                if (group != "face" || key.StartsWith("Face_Nose") || key.StartsWith("Face_Mouth") || key.StartsWith("Face_Lips")) continue;
                var k = key;
                Row(label, 0f, 100f, _draft.GetMorph(k) * 100f, v => $"{v:0}%", v => { _draft.SetMorph(k, v / 100f); Changed(false); }, true);
            }
            var f = _flow;
            f.Gap(4f);
            var b = CzKit.Btn(f.Content, "ResetFace", 0f, f.Y, f.Width, f.S(50), "RESET FEATURES", CzKit.Kind.Plain, () =>
            {
                FreeUiKit.PlayClick();
                foreach (var (key, _, group) in AvatarCatalog.BodyMorphs) if (group == "face") _draft.SetMorph(key, 0f);
                Changed(true);
            }, f.S(20));
            f.Reveal(b);
            f.Y += f.S(60);
        }

        string FaceShapeWord()
        {
            var on = new List<string>();
            foreach (var (key, label, group) in AvatarCatalog.BodyMorphs)
                if (group == "face" && _draft.GetMorph(key) > 0.01f) on.Add(label.ToLowerInvariant());
            return on.Count == 0 ? "As built" : string.Join(", ", on);
        }

        // ------------------------------------------------------------------ HAIR

        string HairName(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                var def = AvatarCatalog.FindHair(OverworldAvatar3d.DefaultHairId(_draft != null && _draft.IsFemale));
                return def != null ? def.name + " (base)" : "Base";
            }
            var h = AvatarCatalog.FindHair(id);
            return h != null ? h.name : id;
        }

        void BuildHairEditor()
        {
            var fem = _draft.IsFemale;
            var cur = _draft.hairStyleId ?? "";
            var defId = OverworldAvatar3d.DefaultHairId(fem);
            var sculpts = new List<AvatarCatalog.HairStyle>();
            var styles = new List<AvatarCatalog.HairStyle>();
            foreach (var h in AvatarCatalog.HairStyles)
            {
                if (h.id == defId) continue;
                (h.id.EndsWith("_3d") ? sculpts : styles).Add(h);
            }
            if (sculpts.Count > 0)
            {
                _flow.Header("Anime sculpts", sculpts.Count.ToString());
                Cards(sculpts.Count, i =>
                {
                    var h = sculpts[i];
                    return (HairThumb(h.id, fem), h.name.Replace(" (sculpt)", ""), cur == h.id, () => { _draft.hairStyleId = h.id; _draft.hairPattern = (int)HairPattern.Original; Changed(true); }, null, null);
                });
            }
            _flow.Header("Styles", (styles.Count + 1).ToString());
            Cards(styles.Count + 1, i =>
            {
                if (i == 0)
                    return (HairThumb(defId, fem), HairName(""), cur.Length == 0, () => { _draft.hairStyleId = ""; Changed(true); }, "BASE", null);
                var h = styles[i - 1];
                return (HairThumb(h.id, fem), h.name, cur == h.id, () => { _draft.hairStyleId = h.id; Changed(true); }, null, null);
            });
        }

        static readonly (string zone, string label)[] HairZones =
        {
            ("root", "Root"), ("tip", "Tips"), ("fringe", "Bangs"), ("streak", "Streaks"), ("side", "Split")
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
            var f = _flow;
            var cur = string.IsNullOrEmpty(_draft.hairStyleId) ? OverworldAvatar3d.DefaultHairId(_draft.IsFemale) : _draft.hairStyleId;
            var style = AvatarCatalog.FindHair(cur);

            // named palettes first: the style's own colours, its canon palette, then every anime / lore palette
            var pals = new List<(string name, AvatarCatalog.HairPaletteEntry pal)> { ("Style colours", null) };
            if (style?.palette != null) pals.Add(("Canon", style.palette));
            foreach (var p in AvatarCatalog.HairPalettes) pals.Add((p.name, p));
            f.Header("Palettes");
            f.Grid(pals.Count, _cols, f.S(64), Gap, (i, x, y, w, h) =>
            {
                var (name, pal) = pals[i];
                var on = pal == null ? _draft.hairPattern == (int)HairPattern.Original : false;
                var b = CzKit.Btn(f.Content, "Pal", x, y, w, h, "", CzKit.Kind.Plain, () =>
                {
                    FreeUiKit.PlaySelect();
                    if (pal == null) _draft.hairPattern = (int)HairPattern.Original; else pal.ApplyTo(_draft);
                    Changed(true);
                });
                var lab = b.transform.Find("Label")?.GetComponent<Text>();
                if (lab != null)
                {
                    lab.text = name; lab.font = CzKit.Body(600); lab.fontSize = f.S(15);
                    lab.alignment = TextAnchor.LowerCenter; lab.rectTransform.offsetMin = new Vector2(4f, 4f);
                    lab.color = on ? CzKit.GoldHi : CzKit.Text;
                    var hv = b.GetComponent<CzHover>();
                    if (hv != null) { hv.LabelRest = lab.color; hv.LabelHover = on ? CzKit.GoldHi : Color.white; }
                }
                // the palette's colours as flat stripes along the top
                var hexes = pal == null ? new List<string>() : new List<string> { pal.root, pal.tip, pal.fringe, pal.streak, pal.side };
                hexes.RemoveAll(string.IsNullOrEmpty);
                if (hexes.Count == 0) hexes.Add(_draft.hairHex);
                var sw = (w - 16f) / hexes.Count;
                for (var k = 0; k < hexes.Count; k++)
                {
                    var st = CzKit.Img(b.transform, "Stripe", UiFoundation.WhiteSprite(), Hex(hexes[k], Color.gray));
                    CzKit.Place(st, 8f + k * sw, 8f, sw - 2f, h * 0.38f);
                }
                if (on) CzKit.GoldFrame(b.transform, 2f, 2.8f);
                f.Reveal(b);
            });

            f.Header("Pattern");
            var pat = (HairPattern)_draft.hairPattern;
            var px = 0f; var py = f.Y; var th = f.S(38);
            for (var i = 0; i < AvatarCatalog.HairPatternNames.Length; i++)
            {
                var p = (HairPattern)i;
                var chip = CzKit.Chip(f.Content, px, py, th, AvatarCatalog.HairPatternNames[i], p == pat, () =>
                {
                    FreeUiKit.PlaySelect();
                    _draft.hairPattern = (int)p;
                    if (!AvatarCatalog.PatternUses(p, _hairZone)) _hairZone = "root";
                    Changed(true);
                }, f.S(18), out var cw);
                if (px > 0f && px + cw > f.Width) { px = 0f; py += th + f.S(6); CzKit.Place((RectTransform)chip.transform, px, py, cw, th); }
                px += cw + f.S(6);
            }
            f.Y = py + th + f.S(10);
            if (pat == HairPattern.Original)
            {
                f.Hint("Original shows the hairstyle as painted (the anime sculpts' own colours). Pick a pattern or a palette to dye it.");
                return;
            }
            f.Header("Zone");
            px = 0f; py = f.Y;
            foreach (var (zone, label) in HairZones)
            {
                if (!AvatarCatalog.PatternUses(pat, zone)) continue;
                var z = zone;
                var chip = CzKit.Chip(f.Content, px, py, th, label, _hairZone == z, () => { FreeUiKit.PlaySelect(); _hairZone = z; Changed(true); }, f.S(18), out var cw);
                var dot = CzKit.Img(chip.transform, "Dot", CzKit.Circle(), Hex(ZoneHex(z), Color.gray));
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                dot.rectTransform.sizeDelta = new Vector2(f.S(10), f.S(10)); dot.rectTransform.anchoredPosition = new Vector2(f.S(8), 0f);
                px += cw + f.S(6);
            }
            if (!AvatarCatalog.PatternUses(pat, _hairZone)) _hairZone = "root";
            f.Y = py + th + f.S(10);
            Gems(AvatarCatalog.HairSwatches, () => ZoneHex(_hairZone), hex => SetZoneHex(_hairZone, hex), false);
            FineTune(() => ZoneHex(_hairZone), hex => SetZoneHex(_hairZone, hex));
        }

        // ------------------------------------------------------------------ OUTFITS

        CharacterLook WearingSignature()
        {
            if (_draft == null) return null;
            for (var i = 0; i < CharacterLooks.All.Length; i++)
                if (CharacterLooks.Wearing(_draft, CharacterLooks.All[i])) return CharacterLooks.All[i];
            return null;
        }

        void BuildOutfitEditor()
        {
            var fem = _draft.IsFemale;
            var looks = new List<CharacterLook>();
            foreach (var l in CharacterLooks.All) if (l.FitsBody(fem)) looks.Add(l);
            var worn = WearingSignature();
            _flow.Header("Signature looks", looks.Count.ToString());
            Cards(looks.Count + 1, i =>
            {
                if (i == 0)
                    return (ItemThumb(ClothingCatalog.DefaultShirt, fem), "Base clothes", worn == null && string.IsNullOrEmpty(_draft.outfitId),
                        () => { ClearSignatureSlots(); Changed(true); }, null, null);
                var look = looks[i - 1];
                var era = look.Id.EndsWith("_dk") ? "DK" : look.Id.EndsWith("_bc") ? "BC" : null;
                return (LookThumb(look, fem), look.Name.Replace("Duelist Kingdom ", "").Replace("Battle City ", ""), worn == look,
                    () => { WearOutfit(look); Changed(true); }, era, null);
            });
            var f = _flow;
            if (worn != null)
            {
                f.Gap(2f);
                var b = CzKit.Btn(f.Content, "Cosplay", 0f, f.Y, f.Width, f.S(54), "FULL COSPLAY: " + worn.Name.ToUpperInvariant(), CzKit.Kind.Gold, () =>
                {
                    FreeUiKit.PlayConfirm();
                    worn.ApplyTo(_draft);
                    _draft.characterId = "";
                    GrantLook(worn);
                    Changed(true);
                }, f.S(17));
                f.Reveal(b);
                f.Y += f.S(62);
                f.Hint("A look dresses your duelist in its pieces. FULL COSPLAY also takes the character's hair, face and height.");
            }
            else f.Hint("A look dresses your duelist in its pieces; your body, face and hair stay yours.");
        }

        /// <summary>The look's seven slots and its garment paints only: body, face, hair and height stay the player's.</summary>
        void WearOutfit(CharacterLook look)
        {
            var tmp = _draft.Clone();
            look.ApplyTo(tmp);
            foreach (ClothingSlot slot in Enum.GetValues(typeof(ClothingSlot))) _draft.SetSlot(slot, tmp.SlotId(slot));
            foreach (var part in new[] { "Shirt", "Bottoms", "Shoes", "Outfit" })
                for (var k = 0; k < 3; k++) _draft.SetPaint(part + "." + k, tmp.GetPaint(part + "." + k));
            _draft.outfitId = "";
            _draft.characterId = "";
            GrantLook(look);
            if (_paintKey.StartsWith("Outfit")) _paintKey = "Shirt.0";
        }

        void GrantLook(CharacterLook look)
        {
            foreach (var id in new[] { look.HatId, look.FacialId, look.ShirtId, look.HandsId, look.BottomsId, look.ShoesId, look.NeckId })
                if (!string.IsNullOrEmpty(id)) _wardrobe?.Grant(id);
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
            _draft.neckId = ClothingCatalog.DefaultNeck;
            if (_paintKey.StartsWith("Outfit")) _paintKey = "Shirt.0";
        }

        // ------------------------------------------------------------------ WARDROBE

        static bool IsSignatureShell(ClothingItem it) => it.Id.EndsWith("_bc") || it.Id.EndsWith("_dk");

        void BuildWardrobeEditor(ClothingSlot slot)
        {
            var fem = _draft.IsFemale;
            var owned = ClothingCatalog.OwnedInSlot(_wardrobe, slot, fem);
            // once the card renders exist (WardrobeThumbs), a piece without one has no model for this body (the
            // Duelist Kingdom star-chip gloves, 2026-10-18): it would change nothing on the duelist, so it is not offered
            if (Thumb(ThumbRes + "body_" + Sex(fem)) != null)
                owned.RemoveAll(it => !it.EmptySlot && Thumb(ThumbRes + "item_" + it.Id + "_" + Sex(fem)) == null);
            var cur = _draft.SlotId(slot);
            var sculpts = owned.FindAll(it => it.Sculpt);
            var shells = owned.FindAll(it => !it.Sculpt && IsSignatureShell(it));
            var basics = owned.FindAll(it => !it.Sculpt && !IsSignatureShell(it));
            basics.Sort((a, b) => (b.EmptySlot ? 1 : 0).CompareTo(a.EmptySlot ? 1 : 0));   // "No hat" first
            void Section(string title, List<ClothingItem> items)
            {
                if (items.Count == 0) return;
                _flow.Header(title, items.Count.ToString());
                Cards(items.Count, i =>
                {
                    var it = items[i];
                    var n = ClothingCatalog.PocketsOnItem(it, _wardrobe);
                    return (ItemThumb(it.Id, fem), it.Name, it.Id == cur, () =>
                    {
                        _draft.SetSlot(slot, it.Id);
                        Changed(true);
                    }, it.CanHoldPockets && n > 0 ? n + (n == 1 ? " pocket" : " pockets") : null, null);
                });
            }
            Section("Anime sculpts", sculpts);
            Section("Signature pieces", shells);
            Section("Basics", basics);
            if (owned.Count <= 1) _flow.Hint("More pieces come from the Bazaar and the story.");
        }

        // ------------------------------------------------------------------ COLOURS

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

        void BuildPaintEditor()
        {
            var outfit = AvatarCatalog.FindOutfit(_draft.outfitId);
            var parts = outfit != null && outfit.FitsBody(_draft.IsFemale) ? AvatarCatalog.OutfitPaintParts : AvatarCatalog.PaintParts;
            if (outfit != null && !_paintKey.StartsWith("Outfit") && !_paintKey.StartsWith("Body")) _paintKey = "Outfit.0";
            if (outfit == null && _paintKey.StartsWith("Outfit")) _paintKey = "Shirt.0";
            var keys = new List<(string key, string label)>();
            foreach (var pp in parts)
                for (var r = 0; r < pp.regions.Length; r++)
                {
                    if (pp.key == "Body" && r == 0) continue;   // skin has its own tab
                    keys.Add((pp.key + "." + r, pp.key == "Body" ? pp.label : pp.label + " " + pp.regions[r]));
                }
            var sculpted = new List<string>();
            foreach (var slot in new[] { ClothingSlot.Shirt, ClothingSlot.Bottoms, ClothingSlot.Shoes })
            {
                var it = ClothingCatalog.Get(_draft.SlotId(slot));
                if (it != null && it.Sculpt) sculpted.Add(it.Name);
            }
            if (sculpted.Count > 0)
                _flow.Hint("Anime sculpts keep their painted colours (" + string.Join(", ", sculpted) + "). Paint applies to the standard pieces.");
            _flow.Header("Region");
            ListRows(keys.Count, i =>
            {
                var (key, label) = keys[i];
                var hex = _draft.GetPaint(key);
                return (label + (string.IsNullOrEmpty(hex) ? "  ·  original" : ""), string.IsNullOrEmpty(hex) ? new Color(0.16f, 0.16f, 0.2f) : Hex(hex, Color.gray),
                    _paintKey == key, () => { _paintKey = key; Changed(true); });
            });
            _flow.Header(PaintLabel(_paintKey));
            Gems(AvatarCatalog.ClothingSwatches, () => _draft.GetPaint(_paintKey), hex => _draft.SetPaint(_paintKey, hex), true);
            FineTune(() => _draft.GetPaint(_paintKey), hex => _draft.SetPaint(_paintKey, hex));
        }

        // ------------------------------------------------------------------ ADORNMENTS

        void BuildAdornmentEditor(bool tattoos)
        {
            var fem = _draft.IsFemale;
            if (tattoos)
            {
                _draft.tattooIds ??= new List<string>();
                var list = new List<TattooItem>();
                foreach (var it in AdornmentCatalog.Tattoos) if (it.FitsBody(fem)) list.Add(it);
                _flow.Header("Tattoos", list.Count.ToString());
                Cards(list.Count, i =>
                {
                    var it = list[i];
                    return (Thumb(it.Resource), it.Name, _draft.tattooIds.Contains(it.Id), () =>
                    {
                        if (_draft.tattooIds.Contains(it.Id)) _draft.tattooIds.Remove(it.Id); else _draft.tattooIds.Add(it.Id);
                        Changed(true);
                    }, null, null);
                });
                if (list.Count == 0) _flow.Hint("No tattoo art is in the catalogue yet. Canon marks (Odion's face, Marik's back) arrive with their sourced art.");
            }
            else
            {
                _draft.accessoryIds ??= new List<string>();
                var list = new List<AccessoryItem>();
                foreach (var it in AdornmentCatalog.Accessories) if (it.FitsBody(fem)) list.Add(it);
                _flow.Header("Accessories", list.Count.ToString());
                Cards(list.Count, i =>
                {
                    var it = list[i];
                    return (null, it.Name, _draft.accessoryIds.Contains(it.Id), () =>
                    {
                        if (_draft.accessoryIds.Contains(it.Id)) _draft.accessoryIds.Remove(it.Id); else _draft.accessoryIds.Add(it.Id);
                        Changed(true);
                    }, null, null);
                });
                if (list.Count == 0) _flow.Hint("No accessories are in the catalogue yet. The Millennium items are worn from WARDROBE · NECK.");
            }
        }

        // ------------------------------------------------------------------ DUELIST

        void BuildTitleEditor()
        {
            _flow.Header("Title");
            var titles = AvatarCatalog.PresetTitles;
            ListRows(titles.Length, i => (titles[i], null, _draft.title == titles[i], () => { _draft.title = titles[i]; Changed(true); }));
        }

        void BuildSpiritEditor()
        {
            _flow.Header("Spirit magic");
            Gems(AvatarCatalog.AccentPresets, () => _draft.accentHex, hex => _draft.accentHex = hex, false);
            _flow.Hint("The colour of your Spirit Dueler's magic. The Duel Disk and the aura follow it unless they have their own palette.");
        }

        void BuildPaletteEditor(bool disk)
        {
            var hexes = disk ? AvatarCatalog.DiskPalettes : AvatarCatalog.AuraPalettes;
            var names = disk ? AvatarCatalog.DiskPaletteNames : AvatarCatalog.AuraPaletteNames;
            var cur = (disk ? _draft.diskHex : _draft.auraHex) ?? "";
            var spirit = Hex(_draft.accentHex, Color.cyan);
            _flow.Header(disk ? "Duel Disk" : "Aura");
            ListRows(hexes.Length, i =>
            {
                var hex = hexes[i];
                return (i < names.Length ? names[i] : hex, hex.Length == 0 ? spirit : Hex(hex, Color.gray),
                    string.Equals(cur, hex, StringComparison.OrdinalIgnoreCase), () =>
                    {
                        if (disk) _draft.diskHex = hex; else _draft.auraHex = hex;
                        Changed(true);
                    });
            });
        }

        void BuildPortraitEditor()
        {
            _flow.Header("Map portrait");
            Cards(AvatarCatalog.PortraitCount, i =>
            {
                var idx = i;
                return (StreamingSprite.Load($"WRLDZ/Avatar/portraits/portrait_{i}.png"), AvatarCatalog.PortraitNames[i], idx == _draft.portraitIndex,
                    () => { _draft.portraitIndex = idx; Changed(true); }, null, null);
            });
            _flow.Hint("The 2D face shown where the 3D body is not drawn (menus, chat, friend lists).");
        }

        // ------------------------------------------------------------------ the detail strip

        (string name, string blurb, string meta) DetailFor(Part p)
        {
            switch (p)
            {
                case Part.Body:
                {
                    var builds = new List<string>();
                    foreach (var (key, label, group) in AvatarCatalog.BodyMorphs)
                        if (group == "build" && _draft.GetMorph(key) > 0.01f) builds.Add(label + " " + Mathf.RoundToInt(_draft.GetMorph(key) * 100f) + "%");
                    return ((_draft.IsFemale ? "Female" : "Male") + " duelist",
                        $"{_draft.EffectiveHeightCm:0} cm  ·  {Feet(_draft.EffectiveHeightCm)}  ·  {WeightWord(_draft.weight * 100f)}",
                        builds.Count == 0 ? "" : string.Join(" · ", builds).ToUpperInvariant());
                }
                case Part.Skin:
                {
                    var hex = _draft.GetPaint("Body.0");
                    return ("Skin tone", string.IsNullOrEmpty(hex) ? "The body's own painted tone." : hex.ToUpperInvariant(), "");
                }
                case Part.FaceShape: return ("Face shape", FaceShapeWord(), "");
                case Part.Eyes:
                {
                    var e = FaceCatalog.Eye(string.IsNullOrEmpty(_draft.eyeId) ? FaceCatalog.DefaultEye : _draft.eyeId);
                    return (e.Name + " eyes", e.Blurb, "IRIS " + (string.IsNullOrEmpty(_draft.irisHex) ? FaceCatalog.DefaultIris : _draft.irisHex).ToUpperInvariant());
                }
                case Part.Brows: { var e = FaceCatalog.Brow(string.IsNullOrEmpty(_draft.browId) ? FaceCatalog.DefaultBrow : _draft.browId); return (e.Name + " brows", e.Blurb, ""); }
                case Part.Nose: { var e = FaceCatalog.Nose(string.IsNullOrEmpty(_draft.noseId) ? FaceCatalog.DefaultNose : _draft.noseId); return (e.Name + " nose", e.Blurb, ""); }
                case Part.Mouth: { var e = FaceCatalog.Mouth(string.IsNullOrEmpty(_draft.mouthId) ? FaceCatalog.DefaultMouth : _draft.mouthId); return (e.Name + " mouth", e.Blurb, ""); }
                case Part.Head: case Part.Ears:
                {
                    var grp = p == Part.Head ? "head" : "ears"; var on = new List<string>();
                    foreach (var (key, label, group) in AvatarCatalog.BodyMorphs) if (group == grp && _draft.GetMorph(key) > 0.01f) on.Add(label + " " + Mathf.RoundToInt(_draft.GetMorph(key) * 100f) + "%");
                    return (p == Part.Head ? "Head type" : "Ears", on.Count == 0 ? "As built." : string.Join(", ", on), "");
                }
                case Part.Hair:
                {
                    var h = AvatarCatalog.FindHair(string.IsNullOrEmpty(_draft.hairStyleId) ? OverworldAvatar3d.DefaultHairId(_draft.IsFemale) : _draft.hairStyleId);
                    return (HairName(_draft.hairStyleId), h != null && !string.IsNullOrEmpty(h.inspiredBy) ? h.inspiredBy : "", "");
                }
                case Part.HairColor:
                    return ("Hair dye", AvatarCatalog.HairPatternNames[Mathf.Clamp(_draft.hairPattern, 0, AvatarCatalog.HairPatternNames.Length - 1)]
                                        + (_draft.hairPattern == 0 ? "" : "  ·  " + _hairZone), "");
                case Part.Outfit:
                {
                    var worn = WearingSignature();
                    return (worn != null ? worn.Name : "Base clothes", worn != null ? "Every piece of this look is worn." : "Your own pieces from the wardrobe.", "");
                }
                case Part.Hat: case Part.Facial: case Part.Neck: case Part.Shirt: case Part.Hands: case Part.Bottoms: case Part.Shoes:
                {
                    var slot = p switch
                    {
                        Part.Hat => ClothingSlot.Hat, Part.Facial => ClothingSlot.Facial, Part.Neck => ClothingSlot.Neck, Part.Shirt => ClothingSlot.Shirt,
                        Part.Hands => ClothingSlot.Hands, Part.Bottoms => ClothingSlot.Bottoms, _ => ClothingSlot.Shoes
                    };
                    var it = ClothingCatalog.Get(_draft.SlotId(slot));
                    if (it == null) return (ClothingCatalog.SlotLabel(slot), "", "");
                    var n = ClothingCatalog.PocketsOnItem(it, _wardrobe);
                    return (it.Name, it.Blurb ?? "", it.CanHoldPockets ? (n + (n == 1 ? " POCKET" : " POCKETS")) + (it.Sculpt ? "  ·  ANIME SCULPT" : "") : it.Sculpt ? "ANIME SCULPT" : "");
                }
                case Part.Paint:
                {
                    var hex = _draft.GetPaint(_paintKey);
                    return (PaintLabel(_paintKey), string.IsNullOrEmpty(hex) ? "Original colour." : hex.ToUpperInvariant(), "");
                }
                case Part.Accessories: return ("Accessories", (_draft.accessoryIds?.Count ?? 0) == 0 ? "None worn." : _draft.accessoryIds.Count + " worn.", "");
                case Part.Tattoos: return ("Tattoos", (_draft.tattooIds?.Count ?? 0) == 0 ? "None." : _draft.tattooIds.Count + " applied.", "");
                case Part.Title: return (string.IsNullOrEmpty(_draft.title) ? "Spirit Dueler" : _draft.title, "Shown under your name to other duelists.", "");
                case Part.Accent: return ("Spirit magic", (_draft.accentHex ?? "").ToUpperInvariant(), "");
                case Part.Disk: return ("Duel Disk", AvatarCatalog.PaletteName(AvatarCatalog.DiskPalettes, AvatarCatalog.DiskPaletteNames, _draft.diskHex), "");
                case Part.Aura: return ("Aura", AvatarCatalog.PaletteName(AvatarCatalog.AuraPalettes, AvatarCatalog.AuraPaletteNames, _draft.auraHex), "");
                case Part.Look:
                    return ("Map portrait", AvatarCatalog.PortraitNames[Mathf.Clamp(_draft.portraitIndex, 0, AvatarCatalog.PortraitCount - 1)], "");
            }
            return ("", "", "");
        }

        // ------------------------------------------------------------------ spirit companion (profile)

        /// <summary>
        /// The spirit companion cards on the profile: players see their Kuriboh; the lab admin account
        /// (<see cref="LabAdminService.IsAdmin"/>) sees all six and taps one to switch (saved on the account, map refreshed).
        /// </summary>
        void RefreshCompanions()
        {
            if (_companionHost == null) return;
            CzKit.Clear(_companionHost);
            var acc = AppSession.Ensure().Account;
            if (acc?.progress == null) return;
            var admin = LabAdminService.IsAdmin(acc);
            var current = KuribohCompanion.CurrentId();
            var list = new List<SpiritCompanions.Def>();
            if (admin) list.AddRange(SpiritCompanions.All);
            else if (SpiritCompanions.Find(current) != null) list.Add(SpiritCompanions.Find(current));
            if (list.Count == 0) return;
            var k = _companionK;
            var w = _companionHost.sizeDelta.x;
            const int cols = 3;
            var gap = 10f * k;
            var cw = (w - gap * (cols - 1)) / cols;
            var ch = Mathf.Round(cw * 1.08f);
            for (var i = 0; i < list.Count; i++)
            {
                var d = list[i];
                var x = (i % cols) * (cw + gap);
                var y = (i / cols) * (ch + gap);
                var id = d.Id;
                var b = CzKit.Card(_companionHost, "Spirit_" + d.Id, x, y, cw, ch, CompanionThumb(d.Id) ?? ImagineAssets.IconSoul(), d.Name, d.Id == current, () =>
                {
                    if (!admin) return;
                    FreeUiKit.PlaySelect();
                    acc.progress.companionId = id;
                    ProgressionService.Persist(acc);
                    RefreshCompanions();
                    _onSaved?.Invoke();   // the map re-attaches its companion
                }, null, null, Mathf.RoundToInt(14 * k));
                b.interactable = admin;
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
    }
}
