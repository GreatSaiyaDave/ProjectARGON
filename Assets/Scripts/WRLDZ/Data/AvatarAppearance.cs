using System;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// Spirit Dueler avatar / trainer look — GO-style customizer data.
    /// Inspired by independent YGO MMOs (e.g. Duel Monsters Online character create)
    /// and Pokémon GO–style trainer photo. Profile frames can use YgoRefs duelist portraits.
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
                title = title,
                vrmId = vrmId,
                use3dPreview = use3dPreview,
                hatId = hatId,
                facialId = facialId,
                shirtId = shirtId,
                handsId = handsId,
                bottomsId = bottomsId,
                shoesId = shoesId
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
            if (string.IsNullOrEmpty(title)) title = "Spirit Dueler";
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

    /// <summary>Named options for the customizer UI.</summary>
    public static class AvatarCatalog
    {
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
            "#F5D0B0", "#E6C8AA", "#C9956B", "#8D5524", "#5C3A21", "#F0C8A0"
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
    }
}
