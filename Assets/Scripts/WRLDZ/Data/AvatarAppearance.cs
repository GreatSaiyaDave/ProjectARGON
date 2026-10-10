using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// Spirit Dueler avatar / trainer look — GO-style customizer data.
    /// Inspired by independent YGO MMOs (e.g. Duel Monsters Online character create)
    /// and Pokémon GO–style trainer photo. Profile frames use original WRLDZ art only; the YgoRefs
    /// reference screenshots are not shipped (tools/OFFBUILD_MANIFEST.md).
    /// </summary>
    [Serializable]
    public class AvatarAppearance
    {
        /// <summary>HQ anime face preset: StreamingAssets/WRLDZ/Avatar/portraits/portrait_{n}.png</summary>
        public int portraitIndex;

        public int bodyIndex;       // legacy layered fallback
        public int hairIndex;
        public int eyesIndex;
        public int outfitIndex;
        public int accessoryIndex;
        public string skinHex = "#E6C8AA";
        public string hairHex = "#2A2A35";
        public string outfitTintHex = "#FFFFFF"; // multiplies outfit layer
        public string accentHex = "#3ECFFF";     // legacy map token + highlights
        /// <summary>Duel Disk body palette (<see cref="AvatarCatalog.DiskPalettes"/>). Empty = match Spirit magic.</summary>
        public string diskHex = "";
        /// <summary>Spirit Dueler aura palette (<see cref="AvatarCatalog.AuraPalettes"/>). Empty = match Spirit magic.</summary>
        public string auraHex = "";
        public string title = "Spirit Dueler";

        /// <summary>Equipped clothing catalog ids (hat / facial / shirt / hands / bottoms / shoes).</summary>
        public string hatId = ClothingCatalog.DefaultHat;
        public string facialId = ClothingCatalog.DefaultFacial;
        public string shirtId = ClothingCatalog.DefaultShirt;
        public string handsId = ClothingCatalog.DefaultHands;
        public string bottomsId = ClothingCatalog.DefaultBottoms;
        public string shoesId = ClothingCatalog.DefaultShoes;

        /// <summary>
        /// Optional 3D avatar stem under StreamingAssets/WRLDZ/Avatar3D/{vrmId}.vrm
        /// (M3 Character Studio / UniVRM pipeline — see AVATAR_3D_INTEGRATION.md).
        /// Empty = 2D portrait / layered only.
        /// </summary>
        public string vrmId = "";

        /// <summary>Prefer 3D VRM when package + file present.</summary>
        public bool use3dPreview;

        /// <summary>
        /// Skyrim-style shape sliders for the modular 3D body, by blendshape name (0..1). Names
        /// come from Resources/WRLDZ/Avatar3D/argon_base_manifest.json; a left/right pair is one
        /// slider (the presenter drives both). Empty = the baked Takahashi preset as built.
        /// </summary>
        public List<MorphWeight> morphs = new();

        [Serializable] public class MorphWeight { public string name; public float weight; }

        // ---- 3D body customization (AVATAR_3D_INTEGRATION.md, "Customization v1") ----

        /// <summary>0 = male base, 1 = female base, -1 = legacy save (derived from body / portrait index).</summary>
        public int bodyType = -1;
        /// <summary>Height in cm; 0 = the base body's own height (175 male / 165 female). Applied as a uniform scale.</summary>
        public float heightCm;
        /// <summary>0 thin .. 0.5 as built .. 1 heavy: drives the Weight_Thin / Weight_Heavy morphs on body and clothes.</summary>
        public float weight = 0.5f;
        /// <summary><see cref="AvatarCatalog.HairStyles"/> id; empty = the base body's own hair.</summary>
        public string hairStyleId = "";
        /// <summary><see cref="AvatarCatalog.Outfits"/> id (a canon duelist's full outfit); empty = the base body's own clothes.</summary>
        public string outfitId = "";
        /// <summary>Face decal id (Resources/WRLDZ/Avatar3D/Faces/&lt;id&gt;.png: eyes + brows drawn over the base face); empty = the base face.</summary>
        public string faceId = "";
        /// <summary>
        /// A complete character model (Resources/WRLDZ/Avatar3D/Characters/&lt;id&gt;, a Humanoid FBX with its own hair,
        /// clothes and face — a canon duelist sourced as a finished model). When set, the Meshy base body, hair grafts,
        /// outfits, paints and face decals are all skipped; only the height scale applies.
        /// </summary>
        public string characterId = "";
        /// <summary>
        /// Draw this body as an animated silhouette (one flat colour, ink outline, no face): the canon duelists until
        /// their real character models exist. The hairstyle and outfit still shape the silhouette.
        /// </summary>
        public bool silhouette;
        public string silhouetteHex = "#12162A";
        /// <summary><see cref="HairPattern"/>. Original keeps the style's painted colours; the others recolour by zone.</summary>
        public int hairPattern;
        /// <summary>Zone colours (the root colour is <see cref="hairHex"/>).</summary>
        public string hairTipHex = "#D0457F";
        public string hairFringeHex = "#E8B84A";
        public string hairStreakHex = "#E8B84A";
        public string hairSideHex = "#3ECFFF";
        /// <summary>Garment recolours by "Part.region" key (Shirt.0 primary / .1 secondary / .2 trim, Bottoms.*, Shoes.*, Body.1 undersuit). Missing = original.</summary>
        public List<PaintSlot> paints = new();

        [Serializable] public class PaintSlot { public string key; public string hex; }

        public bool IsFemale => bodyType >= 0 ? bodyType == 1 : (bodyIndex % 2 == 1 || portraitIndex % 2 == 1);

        public string GetPaint(string key)
        {
            if (paints == null) return "";
            for (var i = 0; i < paints.Count; i++) if (paints[i] != null && paints[i].key == key) return paints[i].hex ?? "";
            return "";
        }

        /// <summary>Empty hex = back to the original painted colour.</summary>
        public void SetPaint(string key, string hex)
        {
            paints ??= new List<PaintSlot>();
            for (var i = 0; i < paints.Count; i++)
                if (paints[i] != null && paints[i].key == key)
                {
                    if (string.IsNullOrEmpty(hex)) paints.RemoveAt(i); else paints[i].hex = hex;
                    return;
                }
            if (!string.IsNullOrEmpty(hex)) paints.Add(new PaintSlot { key = key, hex = hex });
        }

        public float BaseHeightCm => IsFemale ? 165f : 175f;
        public float EffectiveHeightCm => heightCm > 0f ? heightCm : BaseHeightCm;
        /// <summary>Uniform scale the 3D body takes for <see cref="heightCm"/>.</summary>
        public float HeightScale => EffectiveHeightCm / BaseHeightCm;

        public float GetMorph(string name)
        {
            for (var i = 0; i < morphs.Count; i++) if (morphs[i].name == name) return morphs[i].weight;
            return 0f;
        }

        public void SetMorph(string name, float weight)
        {
            weight = Mathf.Clamp01(weight);
            for (var i = 0; i < morphs.Count; i++)
                if (morphs[i].name == name) { if (weight <= 0f) morphs.RemoveAt(i); else morphs[i].weight = weight; return; }
            if (weight > 0f) morphs.Add(new MorphWeight { name = name, weight = weight });
        }

        public static AvatarAppearance Default() => new() { portraitIndex = 0 };

        public AvatarAppearance Clone()
        {
            return new AvatarAppearance
            {
                portraitIndex = portraitIndex,
                bodyIndex = bodyIndex,
                hairIndex = hairIndex,
                eyesIndex = eyesIndex,
                outfitIndex = outfitIndex,
                accessoryIndex = accessoryIndex,
                skinHex = skinHex,
                hairHex = hairHex,
                outfitTintHex = outfitTintHex,
                accentHex = accentHex,
                diskHex = diskHex,
                auraHex = auraHex,
                title = title,
                vrmId = vrmId,
                use3dPreview = use3dPreview,
                hatId = hatId,
                facialId = facialId,
                shirtId = shirtId,
                handsId = handsId,
                bottomsId = bottomsId,
                shoesId = shoesId,
                morphs = morphs != null ? morphs.ConvertAll(m => new MorphWeight { name = m.name, weight = m.weight }) : new List<MorphWeight>(),
                bodyType = bodyType,
                heightCm = heightCm,
                weight = weight,
                hairStyleId = hairStyleId,
                outfitId = outfitId,
                // added 2026-10-04: these four were missing, and every account load / save passes through Clone(),
                // so a saved look lost its face decal, sourced-character id and silhouette on the next session
                faceId = faceId,
                characterId = characterId,
                silhouette = silhouette,
                silhouetteHex = silhouetteHex,
                hairPattern = hairPattern,
                hairTipHex = hairTipHex,
                hairFringeHex = hairFringeHex,
                hairStreakHex = hairStreakHex,
                hairSideHex = hairSideHex,
                paints = paints != null ? paints.ConvertAll(p => new PaintSlot { key = p?.key, hex = p?.hex }) : new List<PaintSlot>()
            };
        }

        public void ClampToCatalog()
        {
            portraitIndex = Mathf.Clamp(portraitIndex, 0, AvatarCatalog.PortraitCount - 1);
            bodyIndex = Mathf.Clamp(bodyIndex, 0, AvatarCatalog.BodyCount - 1);
            hairIndex = Mathf.Clamp(hairIndex, 0, AvatarCatalog.HairCount - 1);
            eyesIndex = Mathf.Clamp(eyesIndex, 0, AvatarCatalog.EyesCount - 1);
            outfitIndex = Mathf.Clamp(outfitIndex, 0, AvatarCatalog.OutfitCount - 1);
            accessoryIndex = Mathf.Clamp(accessoryIndex, 0, AvatarCatalog.AccessoryCount - 1);
            if (string.IsNullOrEmpty(skinHex)) skinHex = "#E6C8AA";
            if (string.IsNullOrEmpty(hairHex)) hairHex = "#2A2A35";
            if (string.IsNullOrEmpty(outfitTintHex)) outfitTintHex = "#FFFFFF";
            if (string.IsNullOrEmpty(accentHex)) accentHex = "#3ECFFF";
            diskHex ??= "";
            auraHex ??= "";
            if (string.IsNullOrEmpty(title)) title = "Spirit Dueler";
            morphs ??= new List<MorphWeight>();
            for (var i = morphs.Count - 1; i >= 0; i--)
            {
                if (morphs[i] == null || string.IsNullOrEmpty(morphs[i].name)) { morphs.RemoveAt(i); continue; }
                morphs[i].weight = Mathf.Clamp01(morphs[i].weight);
            }
            bodyType = Mathf.Clamp(bodyType, -1, 1);
            if (heightCm > 0f) heightCm = Mathf.Clamp(heightCm, AvatarCatalog.HeightMinCm(IsFemale), AvatarCatalog.HeightMaxCm(IsFemale));
            else heightCm = 0f;
            if (float.IsNaN(weight)) weight = 0.5f;
            weight = Mathf.Clamp01(weight);
            hairStyleId ??= "";
            outfitId ??= "";
            hairPattern = Mathf.Clamp(hairPattern, 0, (int)HairPattern.Split);
            if (string.IsNullOrEmpty(hairTipHex)) hairTipHex = "#D0457F";
            if (string.IsNullOrEmpty(hairFringeHex)) hairFringeHex = "#E8B84A";
            if (string.IsNullOrEmpty(hairStreakHex)) hairStreakHex = "#E8B84A";
            if (string.IsNullOrEmpty(hairSideHex)) hairSideHex = "#3ECFFF";
            paints ??= new List<PaintSlot>();
            paints.RemoveAll(p => p == null || string.IsNullOrEmpty(p.key) || string.IsNullOrEmpty(p.hex));
            EnsureClothingDefaults();
        }

        public void EnsureClothingDefaults()
        {
            hatId = SanitizeSlot(ClothingSlot.Hat, hatId);
            facialId = SanitizeSlot(ClothingSlot.Facial, facialId);
            shirtId = SanitizeSlot(ClothingSlot.Shirt, shirtId);
            handsId = SanitizeSlot(ClothingSlot.Hands, handsId);
            bottomsId = SanitizeSlot(ClothingSlot.Bottoms, bottomsId);
            shoesId = SanitizeSlot(ClothingSlot.Shoes, shoesId);
        }

        static string SanitizeSlot(ClothingSlot slot, string id)
        {
            var item = ClothingCatalog.Get(id);
            if (item != null && item.Slot == slot) return item.Id;
            return ClothingCatalog.DefaultId(slot);
        }

        public string SlotId(ClothingSlot slot) => slot switch
        {
            ClothingSlot.Hat => hatId,
            ClothingSlot.Facial => facialId,
            ClothingSlot.Shirt => shirtId,
            ClothingSlot.Hands => handsId,
            ClothingSlot.Bottoms => bottomsId,
            ClothingSlot.Shoes => shoesId,
            _ => ClothingCatalog.DefaultId(slot)
        };

        public void SetSlot(ClothingSlot slot, string id)
        {
            id = SanitizeSlot(slot, id);
            switch (slot)
            {
                case ClothingSlot.Hat: hatId = id; break;
                case ClothingSlot.Facial: facialId = id; break;
                case ClothingSlot.Shirt: shirtId = id; break;
                case ClothingSlot.Hands: handsId = id; break;
                case ClothingSlot.Bottoms: bottomsId = id; break;
                case ClothingSlot.Shoes: shoesId = id; break;
            }
        }

        /// <summary>StreamingAssets relative path for the HQ anime face (no WRLDZ/ prefix for Load helper variants).</summary>
        public string PortraitStreamingPath =>
            $"WRLDZ/Avatar/portraits/portrait_{portraitIndex}.png";

        /// <summary>Full-body face + hair overlay aligned to the paper-doll mannequin.</summary>
        public string LookStreamingPath =>
            $"WRLDZ/Avatar/clothes/look_{portraitIndex}.png";
    }

    /// <summary>How <see cref="AvatarAppearance"/> colours a hairstyle (vertex zone masks baked by tools/custom/hair_build.py).</summary>
    public enum HairPattern { Original = 0, Solid = 1, Tips = 2, TriColor = 3, Streaks = 4, Split = 5 }

    /// <summary>Named options for the customizer UI.</summary>
    public static class AvatarCatalog
    {
        public static float HeightMinCm(bool female) => female ? 145f : 152f;
        public static float HeightMaxCm(bool female) => female ? 190f : 203f;

        public static readonly string[] HairPatternNames = { "Original", "Solid", "Ombré tips", "Tri-color", "Streaks", "Split" };

        /// <summary>Which zone colours a pattern uses (root is always used except Original).</summary>
        public static bool PatternUses(HairPattern p, string zone) => zone switch
        {
            "root" => p != HairPattern.Original,
            "tip" => p is HairPattern.Tips or HairPattern.TriColor,
            "fringe" => p == HairPattern.TriColor,
            "streak" => p == HairPattern.Streaks,
            "side" => p == HairPattern.Split,
            _ => false
        };

        /// <summary>One wearable hairstyle: Resources/WRLDZ/Avatar3D/Hair/argon_hair_{id}_{male|female}.fbx + Textures/Hair_{id}.png.</summary>
        [Serializable]
        public class HairStyle
        {
            public string id;
            public string name;
            public string inspiredBy;
            public string series;
            public string sex;        // "any" | "male" | "female": default suggestion only, every style fits both bodies
            public string length;
            public HairPaletteEntry palette;
        }

        /// <summary>A named multi-colour hair look (pattern + zone colours).</summary>
        [Serializable]
        public class HairPaletteEntry
        {
            public string name;
            public string pattern;   // original | solid | tips | tricolor | streaks | split
            public string root, tip, fringe, streak, side;

            public HairPattern Pattern => (pattern ?? "").ToLowerInvariant() switch
            {
                "solid" => HairPattern.Solid,
                "tips" => HairPattern.Tips,
                "tricolor" => HairPattern.TriColor,
                "streaks" => HairPattern.Streaks,
                "split" => HairPattern.Split,
                _ => HairPattern.Original
            };

            public void ApplyTo(AvatarAppearance a)
            {
                a.hairPattern = (int)Pattern;
                if (!string.IsNullOrEmpty(root)) a.hairHex = root;
                if (!string.IsNullOrEmpty(tip)) a.hairTipHex = tip;
                if (!string.IsNullOrEmpty(fringe)) a.hairFringeHex = fringe;
                if (!string.IsNullOrEmpty(streak)) a.hairStreakHex = streak;
                if (!string.IsNullOrEmpty(side)) a.hairSideHex = side;
            }
        }

        [Serializable] class HairCatalogFile { public List<HairStyle> styles = new(); public List<HairPaletteEntry> palettes = new(); public List<string> wildSwatches = new(); }

        static HairCatalogFile s_hair;
        static HairCatalogFile Hair
        {
            get
            {
                if (s_hair != null) return s_hair;
                var ta = Resources.Load<TextAsset>("WRLDZ/Avatar3D/Hair/hair_catalog");
                try { s_hair = ta != null ? JsonUtility.FromJson<HairCatalogFile>(ta.text) : null; }
                catch (Exception e) { Debug.LogWarning("[AvatarCatalog] hair_catalog.json: " + e.Message); }
                s_hair ??= new HairCatalogFile();
                s_hair.styles ??= new List<HairStyle>();
                s_hair.palettes ??= new List<HairPaletteEntry>();
                s_hair.wildSwatches ??= new List<string>();
                return s_hair;
            }
        }

        /// <summary>One canon outfit: Resources/WRLDZ/Avatar3D/Outfits/argon_outfit_{id}_{sex}.fbx + Outfit_{id}.png / _mask / _{sex}_hide.</summary>
        [Serializable]
        public class OutfitDef
        {
            public string id, name, character, sex;
            /// <summary>The body's undersuit colour under this outfit ("skin" = the skin tone; else a hex): bare forearms, turtlenecks, tights.</summary>
            public string suit;
            public float[] refLum;
            public string[] sexes;   // bodies it was built for
            public bool FitsBody(bool female) { if (sexes == null) return true; foreach (var x in sexes) if (x == (female ? "female" : "male")) return true; return false; }
        }

        [Serializable] class OutfitCatalogFile { public List<OutfitDef> outfits = new(); }
        static OutfitCatalogFile s_outfits;

        /// <summary>Canon duelist outfits (Resources/WRLDZ/Avatar3D/Outfits/outfit_catalog.json).</summary>
        public static IReadOnlyList<OutfitDef> Outfits
        {
            get
            {
                if (s_outfits != null) return s_outfits.outfits;
                var ta = Resources.Load<TextAsset>("WRLDZ/Avatar3D/Outfits/outfit_catalog");
                try { s_outfits = ta != null ? JsonUtility.FromJson<OutfitCatalogFile>(ta.text) : null; }
                catch (Exception e) { Debug.LogWarning("[AvatarCatalog] outfit_catalog.json: " + e.Message); }
                s_outfits ??= new OutfitCatalogFile();
                s_outfits.outfits ??= new List<OutfitDef>();
                return s_outfits.outfits;
            }
        }

        public static OutfitDef FindOutfit(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var o in Outfits) if (o.id == id) return o;
            return null;
        }

        /// <summary>Wearable hairstyles (from Resources/WRLDZ/Avatar3D/Hair/hair_catalog.json). Index 0 of the UI is "Base" (the body's own hair).</summary>
        public static IReadOnlyList<HairStyle> HairStyles => Hair.styles;
        /// <summary>Named anime / lore hair palettes.</summary>
        public static IReadOnlyList<HairPaletteEntry> HairPalettes => Hair.palettes;

        public static HairStyle FindHair(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var h in Hair.styles) if (h.id == id) return h;
            return null;
        }

        /// <summary>Wild anime hair colours for the hair zone swatches (catalogue list, else a built-in set).</summary>
        public static IReadOnlyList<string> HairSwatches => Hair.wildSwatches.Count > 0 ? Hair.wildSwatches : DefaultHairSwatches;

        static readonly string[] DefaultHairSwatches =
        {
            "#141418", "#2A2A35", "#4A3020", "#8B4513", "#C4A35A", "#E8B84A", "#F5E6A0", "#E8E8F0", "#B8C4D6",
            "#D0457F", "#E040A0", "#B01E5A", "#FF3B6B", "#FF6030", "#FF8C1A", "#FFD23E", "#9BE83A", "#2FB57A",
            "#3ECFFF", "#2E7BFF", "#1C2470", "#6A3FD0", "#A15BFF", "#7FD8FF"
        };

        /// <summary>Full clothing palette: a 9-hue x 4-shade grid plus neutrals (the customizer adds an HSV picker for anything else).</summary>
        public static readonly string[] ClothingSwatches =
        {
            "#0B0B10", "#2A2A33", "#55565E", "#8A8C94", "#C4C6CC", "#F2F2F5",
            "#3A0A12", "#7A1428", "#C0303A", "#FF5A6A",
            "#3A1E08", "#7A4012", "#C87028", "#FFB060",
            "#3A3208", "#7A6A12", "#E3B64C", "#FFE27A",
            "#0E2E12", "#1E6A2A", "#2FB57A", "#8CFF9A",
            "#082A30", "#0F5A6A", "#3ECFFF", "#A8F0FF",
            "#0A1638", "#1C2E7A", "#3A7BFF", "#9CC0FF",
            "#1E0E3A", "#43207A", "#7A4CD9", "#C8A8FF",
            "#3A0A2A", "#7A1460", "#E040A0", "#FF9CD8",
            "#4A3020", "#8B5A2B", "#C9956B", "#E8CFA8"
        };

        /// <summary>Paint slots the customizer offers per 3D part, in order: key prefix, label, region labels.</summary>
        public static readonly (string key, string label, string[] regions)[] PaintParts =
        {
            ("Shirt", "Coat", new[] { "Main", "Accent", "Trim" }),
            ("Bottoms", "Pants", new[] { "Main", "Accent", "Trim" }),
            ("Shoes", "Boots", new[] { "Main", "Accent", "Trim" }),
            ("Body", "Undersuit", new[] { "Skin", "Suit" }),
        };

        /// <summary>Paint slots while a canon outfit is worn (one garment, three regions) + the undersuit.</summary>
        public static readonly (string key, string label, string[] regions)[] OutfitPaintParts =
        {
            ("Outfit", "Outfit", new[] { "Main", "Accent", "Trim" }),
            ("Body", "Undersuit", new[] { "Skin", "Suit" }),
        };

        public const int PortraitCount = 8;
        public const int BodyCount = 3;
        public const int HairCount = 4;
        public const int EyesCount = 3;
        public const int OutfitCount = 4;
        public const int AccessoryCount = 4;

        /// <summary>Imagine anime face presets (human duelists).</summary>
        public static readonly string[] PortraitNames =
        {
            "Midnight Ace",
            "Academy Belle",
            "Street Runner",
            "Neon Spark",
            "Ivory Phantom",
            "Gold Braid",
            "Cap Focus",
            "Teal Spirit"
        };

        public static readonly string[] BodyNames = { "Lean", "Broad", "Balanced" };
        public static readonly string[] HairNames = { "Short", "Spiky", "Long", "Cap" };
        public static readonly string[] EyesNames = { "Cool", "Warm", "Focus" };
        public static readonly string[] OutfitNames = { "Street Jacket", "School Coat", "Duel Coat", "Casual Red" };
        public static readonly string[] AccessoryNames = { "None", "Goggles", "Earring", "Scarf" };

        public static readonly string[] PresetTitles =
        {
            "Spirit Dueler",
            "Rookie",
            "Path Walker",
            "Tear Hunter",
            "Shadow Duelist",
            "Arena Bound",
            "King of Games (aspiring)",
            "Referobot Fan"
        };

        public static readonly string[] SkinPresets =
        {
            "#FBE3CF", "#F5D0B0", "#F0C8A0", "#E6C8AA", "#D9A77E", "#C9956B", "#A8704A", "#8D5524", "#6B4226", "#5C3A21", "#3E2716"
        };

        public static readonly string[] HairPresets =
        {
            "#1A1A22", "#2A2A35", "#4A3020", "#8B4513", "#C4A35A", "#E8E8F0",
            "#3ECFFF", "#E040A0", "#FF6030"
        };

        public static readonly string[] AccentPresets =
        {
            "#3ECFFF", "#FF5A7A", "#FFD24A", "#6DFF8A", "#B48CFF", "#FFFFFF"
        };

        /// <summary>Duel Disk body palettes (ghost-glass tint). Index 0 = "" = match Spirit magic.</summary>
        public static readonly string[] DiskPalettes =
        {
            "", "#E8EEF5", "#B7C2CC", "#E3B64C", "#3ECFFF", "#C0303A", "#7A4CD9", "#2FB57A", "#FF7FA8",
            "#3A7BFF", "#4A4F7A"
        };

        public static readonly string[] DiskPaletteNames =
        {
            "Match Spirit magic", "Kaiba Corp White", "Battle City Silver", "Pharaoh Gold", "Spirit Cyan",
            "Slifer Crimson", "Dark Magician Violet", "Emerald", "Kuriboh Rose", "Obelisk Azure", "Shadow Obsidian"
        };

        /// <summary>Aura palettes (always light: the aura only adds light). Index 0 = "" = match Spirit magic.</summary>
        public static readonly string[] AuraPalettes =
        {
            "", "#FFD23E", "#7FD8FF", "#A15BFF", "#FF3B30", "#2E7BFF", "#FFB300", "#3ECFFF", "#FF6FA8",
            "#8CFF4A", "#D8E6FF"
        };

        public static readonly string[] AuraPaletteNames =
        {
            "Match Spirit magic", "Super Saiyan Gold", "Blue-Eyes White", "Dark Magician Violet", "Slifer Crimson",
            "Obelisk Azure", "Winged Dragon of Ra", "Spirit Cyan", "Kuriboh Rose", "Harpie Lime", "Ultra Silver"
        };

        public static string PaletteName(string[] hexes, string[] names, string hex)
        {
            for (var i = 0; i < hexes.Length; i++)
                if (string.Equals(hexes[i], hex ?? "", System.StringComparison.OrdinalIgnoreCase))
                    return i < names.Length ? names[i] : hexes[i];
            return string.IsNullOrEmpty(hex) ? names[0] : hex;
        }
    }
}
