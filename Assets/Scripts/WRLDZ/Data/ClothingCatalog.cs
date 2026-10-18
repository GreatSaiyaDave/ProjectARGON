using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Data
{
    public enum ClothingSlot
    {
        Hat = 0,
        Facial = 1,
        Shirt = 2,
        Hands = 3,
        Bottoms = 4,
        Shoes = 5,
        /// <summary>Cords, pendants and the Millennium items worn at the chest (2026-10-18, Meshy wardrobe).</summary>
        Neck = 6
    }

    /// <summary>Static clothing catalog. IDs persist on the account; stats live here.</summary>
    public sealed class ClothingItem
    {
        public string Id;
        public ClothingSlot Slot;
        public string Name;
        public string Blurb;
        /// <summary>Pockets this garment contributes before tailor extras. Shirt/bottoms only.</summary>
        public int BasePockets;
        /// <summary>Extra pockets the tailor can sew onto this piece (0 = cannot sew).</summary>
        public int MaxSewn;
        public int PriceDigi;
        public bool StarterOwned;
        /// <summary>Hidden "none" slot — no sprite, no pockets.</summary>
        public bool EmptySlot;
        public Color Fallback = Color.white;
        /// <summary>-1 any body, 0 male, 1 female. Signature looks are built for one body.</summary>
        public int BodyType = -1;
        /// <summary>Undersuit hex while this shirt is worn. "skin" follows the skin tone. Empty = no change.</summary>
        public string Suit;
        /// <summary>Albedo stem under Resources/WRLDZ/Avatar3D/Clothes/. Empty = albedo_{Id}.</summary>
        public string Albedo;
        /// <summary>Recolor mask stem in that same folder. Empty = the texture is shown as painted.</summary>
        public string Mask;
        public float RefR = 0.2f, RefG = 0.2f, RefB = 0.2f;

        public bool CanHoldPockets => MaxSewn > 0 || BasePockets > 0;

        public bool FitsBody(bool female) => BodyType < 0 || BodyType == (female ? 1 : 0);

        public string AlbedoStem => string.IsNullOrEmpty(Albedo) ? "albedo_" + Id : Albedo;

        public string ArtPath => EmptySlot ? null : $"WRLDZ/Avatar/clothes/{Id}.png";
    }

    [Serializable]
    public class ClothingSewEntry
    {
        public string itemId;
        public int extraPockets;
    }

    [Serializable]
    public class ClothingWardrobe
    {
        public string[] ownedIds = Array.Empty<string>();
        public ClothingSewEntry[] sewn = Array.Empty<ClothingSewEntry>();

        public void EnsureValid()
        {
            ownedIds ??= Array.Empty<string>();
            sewn ??= Array.Empty<ClothingSewEntry>();
        }

        public bool Owns(string id)
        {
            if (string.IsNullOrEmpty(id) || ownedIds == null) return false;
            for (var i = 0; i < ownedIds.Length; i++)
                if (ownedIds[i] == id) return true;
            return false;
        }

        public int SewnExtra(string id)
        {
            if (string.IsNullOrEmpty(id) || sewn == null) return 0;
            for (var i = 0; i < sewn.Length; i++)
            {
                var e = sewn[i];
                if (e != null && e.itemId == id)
                    return Mathf.Max(0, e.extraPockets);
            }

            return 0;
        }

        public void SetSewnExtra(string id, int extra)
        {
            extra = Mathf.Max(0, extra);
            sewn ??= Array.Empty<ClothingSewEntry>();
            for (var i = 0; i < sewn.Length; i++)
            {
                if (sewn[i] == null || sewn[i].itemId != id) continue;
                sewn[i].extraPockets = extra;
                return;
            }

            var next = new ClothingSewEntry[sewn.Length + 1];
            Array.Copy(sewn, next, sewn.Length);
            next[sewn.Length] = new ClothingSewEntry { itemId = id, extraPockets = extra };
            sewn = next;
        }

        public void Grant(string id)
        {
            if (string.IsNullOrEmpty(id) || Owns(id)) return;
            var next = new string[ownedIds.Length + 1];
            Array.Copy(ownedIds, next, ownedIds.Length);
            next[ownedIds.Length] = id;
            ownedIds = next;
        }
    }

    public static class ClothingCatalog
    {
        public const string DefaultHat = "hat_none";
        public const string DefaultFacial = "face_none";
        public const string DefaultShirt = "shirt_plain";
        public const string DefaultHands = "hands_none";
        public const string DefaultBottoms = "bottoms_plain";
        public const string DefaultShoes = "shoes_plain";
        public const string DefaultNeck = "neck_none";

        public const string BodyArtPath = "WRLDZ/Avatar/clothes/body_full.png";
        public const int MaxOutfitPockets = 6;
        public const int PriceSewnPocketDigi = 500;

        public static readonly ClothingItem[] All =
        {
            // Hats
            N("hat_none", ClothingSlot.Hat, "No hat", "Bare head.", 0, 0, 0, true, true, new Color(0.85f, 0.85f, 0.88f)),
            N("hat_cap", ClothingSlot.Hat, "Street cap", "Soft brim cap.", 0, 0, 280, false, false, new Color(0.18f, 0.22f, 0.38f)),
            N("hat_beanie", ClothingSlot.Hat, "Night beanie", "Knit winter beanie.", 0, 0, 320, false, false, new Color(0.22f, 0.45f, 0.72f)),

            // Facial
            N("face_none", ClothingSlot.Facial, "No facial", "Clear face.", 0, 0, 0, true, true, Color.white),
            N("face_goggles", ClothingSlot.Facial, "Duel goggles", "Tinted riding goggles.", 0, 0, 360, false, false, new Color(0.15f, 0.75f, 0.95f)),
            N("face_visor", ClothingSlot.Facial, "Holo visor", "Slim gold visor.", 0, 0, 420, false, false, new Color(1f, 0.82f, 0.28f)),

            // Shirts — pockets live here
            N("shirt_plain", ClothingSlot.Shirt, "Plain tee", "Default shirt. No pockets.", 0, 2, 0, true, false, new Color(0.82f, 0.84f, 0.88f), "skin"),
            N("shirt_hoodie", ClothingSlot.Shirt, "Path hoodie", "Kangaroo pocket plus a sleeve pocket.", 2, 2, 700, false, false, new Color(0.18f, 0.42f, 0.78f), "skin"),
            N("shirt_cargo", ClothingSlot.Shirt, "Cargo jacket", "Three deck-box pockets stitched in.", 3, 2, 1200, false, false, new Color(0.42f, 0.32f, 0.18f), "#3A3A40"),
            N("shirt_duelcoat", ClothingSlot.Shirt, "Duel coat", "Academy coat with four deep pockets.", 4, 2, 2000, false, false, new Color(0.12f, 0.14f, 0.28f), "#E8ECEF"),

            // Hands
            N("hands_none", ClothingSlot.Hands, "Bare hands", "No gloves.", 0, 0, 0, true, true, Color.white),
            N("hands_gloves", ClothingSlot.Hands, "Fingerless gloves", "Street fingerless pair.", 0, 0, 240, false, false, new Color(0.16f, 0.16f, 0.18f)),
            N("hands_gauntlets", ClothingSlot.Hands, "Disk gauntlets", "Hard-knuckle duel gloves.", 0, 0, 400, false, false, new Color(0.55f, 0.55f, 0.62f)),

            // Duelist Kingdom star chip gloves (fingerless glove + pouch)
            N("hands_yugi_dk", ClothingSlot.Hands, "Yugi's star glove", "Brown fingerless glove with star chip pouch.", 0, 0, 0, false, false, new Color(0.55f, 0.35f, 0.20f), bodyType: 0),
            N("hands_kaiba_dk", ClothingSlot.Hands, "Kaiba's star glove", "Blue-grey fingerless glove with star chip pouch.", 0, 0, 0, false, false, new Color(0.35f, 0.45f, 0.55f), bodyType: 0),
            N("hands_joey_dk", ClothingSlot.Hands, "Joey's star glove", "Red-brown fingerless glove with star chip pouch.", 0, 0, 0, false, false, new Color(0.60f, 0.30f, 0.20f), bodyType: 0),
            N("hands_mai_dk", ClothingSlot.Hands, "Mai's star glove", "Purple fingerless glove with star chip pouch.", 0, 0, 0, false, false, new Color(0.45f, 0.30f, 0.55f), bodyType: 1),
            N("hands_pegasus_dk", ClothingSlot.Hands, "Pegasus's star glove (R)", "White fingerless glove with star chip pouch — right hand.", 0, 0, 0, false, false, new Color(0.90f, 0.88f, 0.85f), bodyType: 0),
            N("hands_panik_dk", ClothingSlot.Hands, "Panik's star glove", "Dark fingerless glove with star chip pouch.", 0, 0, 0, false, false, new Color(0.20f, 0.20f, 0.25f), bodyType: 0),
            N("hands_bonz_dk", ClothingSlot.Hands, "Bonz's star glove", "Grey fingerless glove with star chip pouch.", 0, 0, 0, false, false, new Color(0.50f, 0.50f, 0.52f), bodyType: 0),
            N("hands_mako_dk", ClothingSlot.Hands, "Mako's star glove", "Blue-grey fingerless glove with star chip pouch.", 0, 0, 0, false, false, new Color(0.30f, 0.40f, 0.55f), bodyType: 0),

            // Bottoms — extra pockets
            N("bottoms_plain", ClothingSlot.Bottoms, "Plain pants", "Default pants. One hip pocket for a single deck box.", 1, 2, 0, true, false, new Color(0.22f, 0.24f, 0.32f)),
            N("bottoms_duel", ClothingSlot.Bottoms, "Duel slacks", "One cargo pocket on the thigh.", 1, 2, 600, false, false, new Color(0.14f, 0.16f, 0.28f)),
            N("bottoms_cargo", ClothingSlot.Bottoms, "Cargo pants", "Two deep thigh pockets for deck boxes.", 2, 2, 900, false, false, new Color(0.36f, 0.30f, 0.16f)),

            // Shoes
            N("shoes_plain", ClothingSlot.Shoes, "Street shoes", "Default sneakers.", 0, 0, 0, true, false, new Color(0.90f, 0.90f, 0.92f)),
            N("shoes_kicks", ClothingSlot.Shoes, "Neon kicks", "Cyan-trimmed running shoes.", 0, 0, 340, false, false, new Color(0.20f, 0.85f, 1f)),
            N("shoes_boots", ClothingSlot.Shoes, "Path boots", "Heavy travel boots.", 0, 0, 380, false, false, new Color(0.28f, 0.18f, 0.12f)),

            // Signature Duel Monsters looks. One set of slots per character (CharacterLooks).
            // Yugi and Kaiba are the sculpted outfits, split into slots. The rest are shells on the same bodies.
            C(ClothingSlot.Shirt, "yugi", "bc", 0, "Domino cape", "Battle City blazer worn as a cape over a black tank.", 2, "skin", new Color(0.19f, 0.24f, 0.51f), "albedo_yugi_bc", "mask_yugi_bc", 0.01857f, 0.27375f, 0.82715f),
            C(ClothingSlot.Bottoms, "yugi", "bc", 0, "Domino trousers", "Slim navy school trousers.", 1, null, new Color(0.15f, 0.17f, 0.34f), "albedo_yugi_bc", "mask_yugi_bc", 0.01857f, 0.27375f, 0.82715f),
            C(ClothingSlot.Shoes, "yugi", "bc", 0, "Studded ankle boots", "Black ankle boots.", 0, null, new Color(0.11f, 0.11f, 0.14f), "albedo_yugi_bc", "mask_yugi_bc", 0.01857f, 0.27375f, 0.82715f),

            C(ClothingSlot.Shirt, "kaiba", "bc", 0, "KaibaCorp coat", "White sleeveless Battle City coat over a black turtleneck.", 4, "#0B0B10", new Color(0.93f, 0.94f, 0.94f), "albedo_kaiba_bc", "mask_kaiba_bc", 0.00173f, 0.66066f, 0.70878f),
            C(ClothingSlot.Bottoms, "kaiba", "bc", 0, "Kaiba's black trousers", "Slim black trousers.", 1, null, new Color(0.08f, 0.08f, 0.1f), "albedo_kaiba_bc", "mask_kaiba_bc", 0.00173f, 0.66066f, 0.70878f),
            C(ClothingSlot.Shoes, "kaiba", "bc", 0, "Kaiba's knee boots", "Black knee boots.", 0, null, new Color(0.1f, 0.1f, 0.12f), "albedo_kaiba_bc", "mask_kaiba_bc", 0.00173f, 0.66066f, 0.70878f),

            C(ClothingSlot.Shirt, "joey", "bc", 0, "Battle City tee", "White short-sleeved tee.", 0, "skin", new Color(0.89f, 0.90f, 0.90f)),
            C(ClothingSlot.Bottoms, "joey", "bc", 0, "Blue jeans", "Loose blue jeans.", 1, null, new Color(0.31f, 0.40f, 0.51f)),
            C(ClothingSlot.Shoes, "joey", "bc", 0, "Duel sneakers", "Blue and white sneakers.", 0, null, new Color(0.17f, 0.20f, 0.27f)),

            C(ClothingSlot.Shirt, "mai", "bc", 1, "Cropped vest", "Lavender Battle City vest.", 2, "skin", new Color(0.56f, 0.49f, 0.74f)),
            C(ClothingSlot.Bottoms, "mai", "bc", 1, "Mini skirt", "Purple mini skirt.", 1, null, new Color(0.29f, 0.14f, 0.40f)),
            C(ClothingSlot.Shoes, "mai", "bc", 1, "Mai's knee boots", "Dark knee boots.", 0, null, new Color(0.16f, 0.14f, 0.19f)),
            C(ClothingSlot.Hands, "mai", "bc", 1, "Long gloves", "White fingerless gloves.", 0, null, new Color(0.90f, 0.89f, 0.91f)),

            C(ClothingSlot.Shirt, "tea", "bc", 1, "Sleeveless blouse", "Pale green sleeveless blouse.", 0, "skin", new Color(0.79f, 0.90f, 0.84f)),
            C(ClothingSlot.Bottoms, "tea", "bc", 1, "Short shorts", "Dark shorts.", 1, null, new Color(0.31f, 0.37f, 0.45f)),
            C(ClothingSlot.Shoes, "tea", "bc", 1, "Platform sandals", "Grey-blue platform sandals.", 0, null, new Color(0.267f, 0.443f, 0.553f)),

            // Bakura's undersuit was a dark blue-grey #4F6595 while the coat itself is pale (#8ABED0 from
            // its albedo, and this row's own fallback is #8ABFD1). His canon outfit is pale throughout: his
            // frame measures #BAB4C6 hair, #B4B4A8 torso and #B3ADC2 trousers, and #4F6595 appears in none of
            // it, so the dark band across his exposed neck, chest and scalp read as a foreign garment. The
            // mesh is also a long coat (grafts to z 0.904..1.496, covering the thighs, sleeves and neck),
            // not the short-sleeve overshirt the row described - hence the rename.
            C(ClothingSlot.Shirt, "bakura", "bc", 0, "Bakura's long coat", "Long pale coat over a pale undersuit.", 2, "#8ABED0", new Color(0.54f, 0.75f, 0.82f)),
            C(ClothingSlot.Bottoms, "bakura", "bc", 0, "Bakura's jeans", "Pale lavender jeans.", 1, null, new Color(0.702f, 0.678f, 0.761f)),
            C(ClothingSlot.Shoes, "bakura", "bc", 0, "Bakura's white sneakers", "White sneakers.", 0, null, new Color(0.93f, 0.93f, 0.95f)),

            C(ClothingSlot.Shirt, "marik", "bc", 0, "Sleeveless hoodie", "Lavender sleeveless hoodie.", 2, "skin", new Color(0.82f, 0.80f, 0.91f)),
            C(ClothingSlot.Bottoms, "marik", "bc", 0, "Light trousers", "Beige trousers.", 1, null, new Color(0.90f, 0.86f, 0.76f)),
            C(ClothingSlot.Shoes, "marik", "bc", 0, "Black shoes", "Black shoes.", 0, null, new Color(0.118f, 0.118f, 0.118f)),

            C(ClothingSlot.Shirt, "pegasus", "dk", 0, "Red frock coat", "Duelist Kingdom red suit coat.", 4, "#E8ECEF", new Color(0.73f, 0.35f, 0.35f)),
            C(ClothingSlot.Bottoms, "pegasus", "dk", 0, "Crimson trousers", "Crimson suit trousers.", 1, null, new Color(0.66f, 0.30f, 0.31f)),
            C(ClothingSlot.Shoes, "pegasus", "dk", 0, "Dress shoes", "Silver dress shoes.", 0, null, new Color(0.72f, 0.74f, 0.77f)),

            // Weevil wears long black trousers in DK. argon_bottoms_weevil_bc_male.fbx is the long base trouser
            // silhouette (z 0.186..1.218, albedo #222324) and its rebuilt hide covers both legs, so the shins are
            // hidden by the trousers themselves - no undersuit trick needed. With the legs covered, the only Suit
            // area still visible is the neck/collar (plus a sliver at the sleeve hem), so the hex is the jacket's
            // own rendered green - the blurb says it is closed to the neck - and not the trousers' black.
            C(ClothingSlot.Shirt, "weevil", "bc", 0, "Closed jacket", "Green jacket closed to the neck.", 3, "#86B05A", new Color(0.53f, 0.69f, 0.35f)),
            C(ClothingSlot.Bottoms, "weevil", "bc", 0, "Black trousers", "Black trousers.", 1, null, new Color(0.133f, 0.137f, 0.141f)),
            C(ClothingSlot.Shoes, "weevil", "bc", 0, "Weevil's sneakers", "Dark sneakers.", 0, null, new Color(0.25f, 0.26f, 0.24f)),
            C(ClothingSlot.Facial, "weevil", "bc", 0, "Round glasses", "Round glasses.", 0, null, new Color(0.890f, 0.784f, 0.290f)),

            C(ClothingSlot.Shirt, "rex", "bc", 0, "Open jacket", "Green open jacket.", 2, "#D7D09F", new Color(0.23f, 0.49f, 0.38f)),
            C(ClothingSlot.Bottoms, "rex", "bc", 0, "Brown trousers", "Tan trousers.", 1, null, new Color(0.59f, 0.48f, 0.33f)),
            C(ClothingSlot.Shoes, "rex", "bc", 0, "Rex's sneakers", "Dark sneakers.", 0, null, new Color(0.16f, 0.16f, 0.15f)),
            C(ClothingSlot.Hat, "rex", "bc", 0, "Red beanie", "Red beanie.", 0, null, new Color(0.55f, 0.18f, 0.18f)),

            C(ClothingSlot.Shirt, "mako", "bc", 0, "Fisherman wrap", "Sleeveless wrap top.", 2, "skin", new Color(0.50f, 0.56f, 0.82f)),
            C(ClothingSlot.Bottoms, "mako", "bc", 0, "Mako's spats", "Grey spats.", 1, null, new Color(0.71f, 0.72f, 0.77f)),
            C(ClothingSlot.Shoes, "mako", "bc", 0, "Mako's sandals", "Straw sandals.", 0, null, new Color(0.42f, 0.33f, 0.27f)),
            C(ClothingSlot.Hat, "mako", "bc", 0, "Wave headband", "Dark blue headband.", 0, null, new Color(0.11f, 0.14f, 0.44f)),

            C(ClothingSlot.Shirt, "keith", "dk", 0, "Biker vest", "Dark vest over a short-sleeve shirt.", 2, "#82302A", new Color(0.19f, 0.20f, 0.29f)),
            C(ClothingSlot.Bottoms, "keith", "dk", 0, "Keith's trousers", "Tan trousers.", 1, null, new Color(0.85f, 0.80f, 0.66f)),
            C(ClothingSlot.Shoes, "keith", "dk", 0, "Tall boots", "Dark brown boots.", 0, null, new Color(0.24f, 0.14f, 0.13f)),
            C(ClothingSlot.Hands, "keith", "dk", 0, "Studded wristbands", "Silver studded wristbands.", 0, null, new Color(0.68f, 0.68f, 0.75f)),
            C(ClothingSlot.Hat, "keith", "dk", 0, "Keith's bandana", "Red bandana.", 0, null, new Color(0.70f, 0.23f, 0.23f)),

            // Bonz wears grey, not violet: bonz_e18_1106.png (his Joey duel) measures #8A8B8E across the tunic and
            // #8A8B8D across the legs - the same grey - while this row read "Violet shorts" #605A9D. That frame also
            // shows full-length legs (a narrow grey band down to the ankles, blue shoes below it, no skin), so the
            // undersuit hex and the bottoms are the tunic's own grey. The bottoms mesh is now the long base trouser.
            C(ClothingSlot.Shirt, "bonz", "dk", 0, "Hooded tunic", "Graveyard tunic.", 2, "#8A8B8D", new Color(0.54f, 0.55f, 0.56f)),
            C(ClothingSlot.Bottoms, "bonz", "dk", 0, "Grey trousers", "Grey trousers.", 1, null, new Color(0.541f, 0.545f, 0.553f)),
            C(ClothingSlot.Shoes, "bonz", "dk", 0, "Bonz's sneakers", "Blue sneakers.", 0, null, new Color(0.36f, 0.55f, 0.69f)),
            C(ClothingSlot.Hat, "bonz", "dk", 0, "Tunic hood", "Hood up.", 0, null, new Color(0.54f, 0.55f, 0.56f)),

            // Duelist Kingdom protagonists' set — the meshes + albedos shipped but had no catalogue row,
            // so GraftCloth could not load them. Fallbacks match the authored albedo swatches.
            C(ClothingSlot.Shirt, "kaiba", "dk", 0, "Blue-violet tunic", "Blue-violet Duelist Kingdom top over a black high-neck shirt.", 4, "#26272B", new Color(0.267f, 0.239f, 0.478f)),
            C(ClothingSlot.Bottoms, "kaiba", "dk", 0, "Teal trousers", "Teal Duelist Kingdom trousers.", 1, null, new Color(0.212f, 0.318f, 0.369f)),
            C(ClothingSlot.Shoes, "kaiba", "dk", 0, "Kaiba's boots", "Black Duelist Kingdom boots.", 0, null, new Color(0.200f, 0.208f, 0.208f)),

            C(ClothingSlot.Shirt, "yugi", "dk", 0, "School blazer", "Domino High blazer over the black tank.", 4, "#1F1E2A", new Color(0.251f, 0.302f, 0.588f)),
            C(ClothingSlot.Bottoms, "yugi", "dk", 0, "School trousers", "Domino High trousers.", 1, null, new Color(0.251f, 0.302f, 0.588f)),
            C(ClothingSlot.Shoes, "yugi", "dk", 0, "School shoes", "Pale Domino High shoes.", 0, null, new Color(0.843f, 0.851f, 0.831f)),

            C(ClothingSlot.Shirt, "yami", "dk", 0, "Dark school blazer", "Domino High blazer in Yami shading.", 4, "#1F1E2A", new Color(0.129f, 0.133f, 0.459f)),
            C(ClothingSlot.Bottoms, "yami", "dk", 0, "Dark school trousers", "Domino High trousers in Yami shading.", 1, null, new Color(0.129f, 0.133f, 0.459f)),

            C(ClothingSlot.Bottoms, "mako", "dk", 0, "Fisherman spats", "Dark spats under the wrap top.", 1, null, new Color(0.306f, 0.290f, 0.310f)),

            C(ClothingSlot.Shirt, "ishizu", "bc", 1, "White dress", "Sleeveless Battle City dress.", 2, "skin", new Color(0.92f, 0.90f, 0.83f)),
            C(ClothingSlot.Bottoms, "ishizu", "bc", 1, "Dress skirt", "Long pale skirt.", 1, null, new Color(0.89f, 0.88f, 0.80f)),
            C(ClothingSlot.Shoes, "ishizu", "bc", 1, "Ishizu's sandals", "Gold sandals.", 0, null, new Color(0.77f, 0.64f, 0.37f)),
            C(ClothingSlot.Hat, "ishizu", "bc", 1, "Ishizu's veil", "Pale veil.", 0, null, new Color(0.92f, 0.90f, 0.83f)),

            C(ClothingSlot.Shirt, "odion", "bc", 0, "Rare Hunter cloak", "Hooded purple cloak.", 4, "#4E3D63", new Color(0.25f, 0.21f, 0.34f)),
            C(ClothingSlot.Bottoms, "odion", "bc", 0, "Dark trousers", "Trousers under the cloak.", 1, null, new Color(0.16f, 0.14f, 0.20f)),
            C(ClothingSlot.Shoes, "odion", "bc", 0, "Odion's boots", "Dark boots.", 0, null, new Color(0.10f, 0.09f, 0.11f)),
            C(ClothingSlot.Hat, "odion", "bc", 0, "Cloak hood", "Hood up.", 0, null, new Color(0.25f, 0.21f, 0.34f)),

            C(ClothingSlot.Shirt, "duke", "bc", 0, "Long vest", "Sleeveless red vest.", 2, "skin", new Color(0.58f, 0.23f, 0.25f)),
            C(ClothingSlot.Bottoms, "duke", "bc", 0, "Duke's black trousers", "Black trousers.", 1, null, new Color(0.15f, 0.16f, 0.18f)),
            C(ClothingSlot.Shoes, "duke", "bc", 0, "Duke's shoes", "Black shoes.", 0, null, new Color(0.16f, 0.16f, 0.16f)),

            C(ClothingSlot.Shirt, "tristan", "bc", 0, "Long coat", "Tan Battle City coat.", 4, "#E6ECF0", new Color(0.40f, 0.36f, 0.26f)),
            C(ClothingSlot.Bottoms, "tristan", "bc", 0, "Tristan's jeans", "Dark jeans.", 1, null, new Color(0.17f, 0.16f, 0.18f)),
            C(ClothingSlot.Shoes, "tristan", "bc", 0, "Tristan's white sneakers", "White sneakers.", 0, null, new Color(0.90f, 0.93f, 0.94f)),

            C(ClothingSlot.Shirt, "mokuba", "bc", 0, "Puffer vest", "Gold vest over a striped shirt.", 2, "#AFCFEE", new Color(0.89f, 0.76f, 0.50f)),
            C(ClothingSlot.Bottoms, "mokuba", "bc", 0, "Mokuba's jeans", "Blue jeans.", 1, null, new Color(0.310f, 0.353f, 0.525f)),
            C(ClothingSlot.Shoes, "mokuba", "bc", 0, "Mokuba's sneakers", "Blue sneakers.", 0, null, new Color(0.306f, 0.471f, 0.722f)),

            // ---- Meshy image-to-3D sculpts (Assets/Models/Characters, 2026-10-17), fitted onto the bodies by
            // Tools/avatar/meshy_wardrobe.py. Painted pieces: no recolour mask, the texture shows as painted. Ids end
            // in _3d; the shell versions above stay in the catalogue. Signature pieces are granted with their look.
            N("shirt_kaiba_3d", ClothingSlot.Shirt, "Kaiba's trench coat", "Indigo Duelist Kingdom trench coat over the teal shirt.", 4, 2, 0, false, false, new Color(0.16f, 0.17f, 0.42f), "#2B6068", 0),
            N("shoes_kaiba_3d", ClothingSlot.Shoes, "Kaiba's shoes", "Black dress shoes.", 0, 0, 0, false, false, new Color(0.08f, 0.08f, 0.09f), null, 0),
            N("shirt_yugi_3d", ClothingSlot.Shirt, "Domino High jacket", "Blue Domino High jacket over the black shirt (buttoned on the female body).", 2, 2, 0, false, false, new Color(0.15f, 0.30f, 0.62f), "#0B0B10"),
            N("bottoms_yugi_3d", ClothingSlot.Bottoms, "Domino High trousers", "Blue Domino High trousers.", 1, 2, 0, false, false, new Color(0.13f, 0.26f, 0.58f), null, 0),
            N("shoes_yugi_3d", ClothingSlot.Shoes, "Yugi's sneakers", "Navy-and-white sneakers.", 0, 0, 0, false, false, new Color(0.18f, 0.22f, 0.40f), null, 0),
            N("shirt_joey_3d", ClothingSlot.Shirt, "Joey's jacket", "Green Duelist Kingdom jacket over a white tee.", 2, 2, 0, false, false, new Color(0.13f, 0.52f, 0.33f), "#F2F2F5", 0),
            N("shoes_joey_3d", ClothingSlot.Shoes, "Joey's loafers", "Black loafers.", 0, 0, 0, false, false, new Color(0.10f, 0.10f, 0.11f), null, 0),
            N("shirt_bakura_3d", ClothingSlot.Shirt, "Bakura's sweater", "Cream V-neck sweater over a green collared shirt.", 0, 0, 0, false, false, new Color(0.88f, 0.86f, 0.78f), "#2E7D4F", 0),
            N("bottoms_bakura_3d", ClothingSlot.Bottoms, "Bakura's jeans", "Blue denim jeans.", 1, 2, 0, false, false, new Color(0.20f, 0.36f, 0.58f), null, 0),
            N("shoes_bakura_3d", ClothingSlot.Shoes, "Bakura's sneakers", "Blue-and-white canvas sneakers.", 0, 0, 0, false, false, new Color(0.22f, 0.40f, 0.68f), null, 0),
            N("hat_keith_3d", ClothingSlot.Hat, "Keith's bandana", "Stars-and-stripes bandana over his hair.", 0, 0, 0, false, false, new Color(0.70f, 0.20f, 0.22f), null, 0),
            N("shirt_keith_3d", ClothingSlot.Shirt, "Keith's vest", "Studded navy vest over a red tee.", 2, 2, 0, false, false, new Color(0.16f, 0.18f, 0.36f), "#B32424", 0),
            N("bottoms_keith_3d", ClothingSlot.Bottoms, "Keith's cargo pants", "Khaki cargo trousers.", 2, 2, 0, false, false, new Color(0.72f, 0.66f, 0.50f), null, 0),
            N("shoes_keith_3d", ClothingSlot.Shoes, "Keith's combat boots", "Black combat boots.", 0, 0, 0, false, false, new Color(0.09f, 0.09f, 0.10f), null, 0),
            N("shirt_bonz_3d", ClothingSlot.Shirt, "Bonz's hoodie", "Olive hoodie, hood down.", 2, 2, 0, false, false, new Color(0.62f, 0.62f, 0.33f), null, 0),
            N("bottoms_bonz_3d", ClothingSlot.Bottoms, "Bonz's shorts", "Violet shorts.", 1, 2, 0, false, false, new Color(0.36f, 0.30f, 0.62f), null, 0),
            N("shoes_bonz_3d", ClothingSlot.Shoes, "Bonz's sneakers", "Blue padded high-tops.", 0, 0, 0, false, false, new Color(0.16f, 0.36f, 0.62f), null, 0),
            N("shirt_mokuba_3d", ClothingSlot.Shirt, "Mokuba's top", "Red top with striped sleeves and a blue bandana.", 0, 0, 0, false, false, new Color(0.78f, 0.18f, 0.16f), null, 0),
            N("bottoms_mokuba_3d", ClothingSlot.Bottoms, "Mokuba's trousers", "Teal trousers.", 1, 2, 0, false, false, new Color(0.55f, 0.78f, 0.72f), null, 0),
            N("shoes_mokuba_3d", ClothingSlot.Shoes, "Mokuba's skate sneakers", "Blue-and-black skate sneakers.", 0, 0, 0, false, false, new Color(0.14f, 0.26f, 0.60f), null, 0),
            N("hat_panik_3d", ClothingSlot.Hat, "PaniK's cap", "Navy cap with his winged hair.", 0, 0, 0, false, false, new Color(0.12f, 0.16f, 0.30f), null, 0),
            N("shirt_panik_3d", ClothingSlot.Shirt, "PaniK's coat", "Dark trench coat with silver trim.", 4, 2, 0, false, false, new Color(0.10f, 0.11f, 0.18f), "#141018", 0),
            N("bottoms_panik_3d", ClothingSlot.Bottoms, "PaniK's trousers", "Black trousers.", 1, 2, 0, false, false, new Color(0.08f, 0.08f, 0.09f), null, 0),
            N("shoes_panik_3d", ClothingSlot.Shoes, "PaniK's boots", "Black ankle-strap boots.", 0, 0, 0, false, false, new Color(0.08f, 0.08f, 0.09f), null, 0),
            N("shirt_pegasus_3d", ClothingSlot.Shirt, "Pegasus's suit", "Red suit jacket with a ruffled cravat.", 2, 2, 0, false, false, new Color(0.70f, 0.12f, 0.14f), "#F2F2F5", 0),
            N("bottoms_pegasus_3d", ClothingSlot.Bottoms, "Pegasus's slacks", "Red suit trousers.", 1, 2, 0, false, false, new Color(0.66f, 0.11f, 0.13f), null, 0),
            N("shoes_pegasus_3d", ClothingSlot.Shoes, "Pegasus's shoes", "White dress shoes.", 0, 0, 0, false, false, new Color(0.92f, 0.92f, 0.90f), null, 0),
            N("shoes_rex_3d", ClothingSlot.Shoes, "Rex's sneakers", "Blue-and-white sneakers.", 0, 0, 0, false, false, new Color(0.20f, 0.36f, 0.72f), null, 0),
            N("bottoms_mako_3d", ClothingSlot.Bottoms, "Mako's jeans", "Cropped navy jeans.", 1, 2, 0, false, false, new Color(0.16f, 0.20f, 0.34f), null, 0),
            N("shoes_mako_3d", ClothingSlot.Shoes, "Mako's boots", "Brown cuffed fisherman boots.", 0, 0, 0, false, false, new Color(0.42f, 0.28f, 0.18f), null, 0),
            N("hat_shadi_3d", ClothingSlot.Hat, "Shadi's turban", "White turban.", 0, 0, 0, false, false, new Color(0.90f, 0.90f, 0.92f), null, 0),
            N("shirt_shadi_3d", ClothingSlot.Shirt, "Shadi's robe", "White hooded robe, full length.", 0, 0, 0, false, false, new Color(0.92f, 0.92f, 0.90f), "#F2F0EA", 0),
            N("shoes_shadi_3d", ClothingSlot.Shoes, "Shadi's shoes", "White ankle shoes.", 0, 0, 0, false, false, new Color(0.90f, 0.90f, 0.88f), null, 0),
            N("hat_para_head_3d", ClothingSlot.Hat, "Para's head", "Para: the bald head with 迷 on the brow (replaces the head).", 0, 0, 0, false, false, new Color(0.80f, 0.60f, 0.44f), null, 0),
            N("shirt_para_3d", ClothingSlot.Shirt, "Para's tunic", "Orange tunic with black sleeves.", 0, 0, 0, false, false, new Color(0.78f, 0.36f, 0.14f), "#B9541F", 0),
            N("hat_dox_head_3d", ClothingSlot.Hat, "Dox's head", "Dox: the bald head with 宮 on the brow (replaces the head).", 0, 0, 0, false, false, new Color(0.80f, 0.60f, 0.44f), null, 0),
            N("shirt_dox_3d", ClothingSlot.Shirt, "Dox's tunic", "Green tunic with black sleeves.", 0, 0, 0, false, false, new Color(0.18f, 0.52f, 0.34f), "#2A7A50", 0),
            N("face_weevil_3d", ClothingSlot.Facial, "Weevil's glasses", "Round gold glasses with the horned bridge.", 0, 0, 0, false, false, new Color(0.84f, 0.70f, 0.24f), null, 0),
            N("shirt_weevil_3d", ClothingSlot.Shirt, "Weevil's tailcoat", "Green tailcoat with a red bow tie.", 2, 2, 0, false, false, new Color(0.48f, 0.62f, 0.22f), "#F2F2F5", 0),
            N("bottoms_weevil_3d", ClothingSlot.Bottoms, "Weevil's trousers", "Black trousers.", 1, 2, 0, false, false, new Color(0.10f, 0.10f, 0.11f), null, 0),
            N("shirt_mai_3d", ClothingSlot.Shirt, "Mai's jacket", "Purple jacket over the white corset top.", 2, 2, 0, false, false, new Color(0.46f, 0.26f, 0.60f), "#F2F2F5", 1),
            N("bottoms_mai_3d", ClothingSlot.Bottoms, "Mai's skirt", "Purple mini skirt with belt.", 0, 0, 0, false, false, new Color(0.40f, 0.22f, 0.56f), null, 1),
            N("shoes_mai_3d", ClothingSlot.Shoes, "Mai's boots", "Purple thigh-high boots.", 0, 0, 0, false, false, new Color(0.38f, 0.22f, 0.54f), null, 1),
            N("shirt_tea_3d", ClothingSlot.Shirt, "Domino High blazer", "Pink Domino High blazer with a blue bow.", 2, 2, 0, false, false, new Color(0.86f, 0.36f, 0.52f), "#F2F2F5", 1),
            N("bottoms_tea_3d", ClothingSlot.Bottoms, "Domino High skirt", "Blue pleated uniform skirt.", 0, 0, 0, false, false, new Color(0.18f, 0.36f, 0.68f), null, 1),
            N("shoes_tea_3d", ClothingSlot.Shoes, "Uniform loafers", "Brown loafers with white socks.", 0, 0, 0, false, false, new Color(0.40f, 0.24f, 0.14f), null, 1),
            // player shop pieces (both bodies)
            N("shirt_crimson_3d", ClothingSlot.Shirt, "Crimson tech jacket", "Black jacket with crimson panels.", 2, 2, 1400, false, false, new Color(0.55f, 0.10f, 0.12f), "#0B0B10"),
            N("bottoms_techwear_3d", ClothingSlot.Bottoms, "Techwear cargo pants", "Black cargo trousers with red trim.", 2, 2, 900, false, false, new Color(0.10f, 0.10f, 0.11f)),
            N("shoes_techwear_3d", ClothingSlot.Shoes, "Techwear sneakers", "Black-and-red high-top sneakers.", 0, 0, 380, false, false, new Color(0.14f, 0.12f, 0.12f)),

            // Neck
            N("neck_none", ClothingSlot.Neck, "Bare neck", "Nothing worn at the neck.", 0, 0, 0, true, true, Color.white),
            N("neck_yugi_3d", ClothingSlot.Neck, "Puzzle cord", "Leather cord for the Millennium Puzzle.", 0, 0, 0, false, false, new Color(0.45f, 0.26f, 0.14f)),
            N("neck_mring_3d", ClothingSlot.Neck, "Millennium Ring", "The Millennium Ring on its cord.", 0, 0, 0, false, false, new Color(0.86f, 0.70f, 0.22f)),
            N("neck_mkey_3d", ClothingSlot.Neck, "Millennium Key", "The Millennium Key, worn at the chest.", 0, 0, 0, false, false, new Color(0.86f, 0.70f, 0.22f), null, 0),
        };

        static ClothingItem N(string id, ClothingSlot slot, string name, string blurb,
            int pockets, int maxSewn, int price, bool starter, bool empty, Color fallback, string suit = null, int bodyType = -1) =>
            new()
            {
                Suit = suit,
                Id = id,
                Slot = slot,
                Name = name,
                Blurb = blurb,
                BasePockets = pockets,
                MaxSewn = maxSewn,
                PriceDigi = price,
                StarterOwned = starter,
                EmptySlot = empty,
                Fallback = fallback,
                BodyType = bodyType
            };

        /// <summary>A signature piece. Id is {slot}_{character}_{arc}, for example shirt_yugi_bc.</summary>
        static ClothingItem C(ClothingSlot slot, string character, string arc, int body, string name, string blurb,
            int pockets, string suit, Color fallback, string albedo = null, string mask = null,
            float refR = 0.2f, float refG = 0.2f, float refB = 0.2f) =>
            new()
            {
                Id = SlotPrefix(slot) + "_" + character + "_" + arc,
                Slot = slot,
                Name = name,
                Blurb = blurb,
                BasePockets = pockets,
                MaxSewn = pockets > 0 ? 2 : 0,
                PriceDigi = 0,
                StarterOwned = false,
                EmptySlot = false,
                Fallback = fallback,
                BodyType = body,
                Suit = suit,
                Albedo = albedo,
                Mask = mask,
                RefR = refR,
                RefG = refG,
                RefB = refB
            };

        static string SlotPrefix(ClothingSlot slot) => slot switch
        {
            ClothingSlot.Hat => "hat",
            ClothingSlot.Facial => "face",
            ClothingSlot.Shirt => "shirt",
            ClothingSlot.Hands => "hands",
            ClothingSlot.Bottoms => "bottoms",
            ClothingSlot.Neck => "neck",
            _ => "shoes"
        };

        public static ClothingItem Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];
            return null;
        }

        public static string DefaultId(ClothingSlot slot) => slot switch
        {
            ClothingSlot.Hat => DefaultHat,
            ClothingSlot.Facial => DefaultFacial,
            ClothingSlot.Shirt => DefaultShirt,
            ClothingSlot.Hands => DefaultHands,
            ClothingSlot.Bottoms => DefaultBottoms,
            ClothingSlot.Shoes => DefaultShoes,
            ClothingSlot.Neck => DefaultNeck,
            _ => DefaultHat
        };

        public static IEnumerable<ClothingItem> InSlot(ClothingSlot slot)
        {
            for (var i = 0; i < All.Length; i++)
                if (All[i].Slot == slot)
                    yield return All[i];
        }

        /// <summary>
        /// Hats with a crown (caps, beanies, hoods, Ishizu's veil) sit where the hairstyle is: the grafted hair is hidden
        /// while one is worn, so spikes never pierce the hat (the veil is a closed dome over the skull, like a hood).
        /// Bands leave the hair (they sit over it with a hair allowance).
        /// </summary>
        public enum HairClip { None, Rim, Hood }

        /// <summary>How a worn hat cuts the hairstyle: Rim = caps and beanies (the hair above the rim plane, forehead to
        /// nape, is hidden; fringe, sideburns and long hair below stay), Hood = hoods and Ishizu's veil (everything above
        /// the hood's lower edge at the neck is hidden; long hair falls out underneath).</summary>
        public static HairClip HatHairClip(string hatId) => hatId switch
        {
            "hat_cap" or "hat_beanie" or "hat_rex_bc" => HairClip.Rim,
            "hat_bonz_dk" or "hat_odion_bc" or "hat_ishizu_bc" => HairClip.Hood,
            // the Meshy headwear carries its own hair (Keith, PaniK) or replaces the whole head (Para, Dox)
            "hat_keith_3d" or "hat_panik_3d" or "hat_shadi_3d" or "hat_para_head_3d" or "hat_dox_head_3d" => HairClip.Hood,
            _ => HairClip.None
        };

        public static bool HatHidesHair(string hatId) => hatId switch
        {
            "hat_cap" or "hat_beanie" or "hat_rex_bc" or "hat_bonz_dk" or "hat_odion_bc" or "hat_ishizu_bc" => true,
            "hat_keith_3d" or "hat_panik_3d" or "hat_shadi_3d" or "hat_para_head_3d" or "hat_dox_head_3d" => true,
            _ => false
        };

        public static List<ClothingItem> OwnedInSlot(ClothingWardrobe wardrobe, ClothingSlot slot, bool? female = null)
        {
            var list = new List<ClothingItem>();
            for (var i = 0; i < All.Length; i++)
            {
                var it = All[i];
                if (it.Slot != slot) continue;
                if (female.HasValue && !it.FitsBody(female.Value)) continue;
                if (it.StarterOwned || (wardrobe != null && wardrobe.Owns(it.Id)))
                    list.Add(it);
            }

            if (list.Count == 0)
            {
                var fallback = Get(DefaultId(slot));
                if (fallback != null) list.Add(fallback);
            }

            return list;
        }

        public static int PocketsOnItem(ClothingItem item, ClothingWardrobe wardrobe)
        {
            if (item == null) return 0;
            var extra = wardrobe != null ? wardrobe.SewnExtra(item.Id) : 0;
            extra = Mathf.Clamp(extra, 0, Mathf.Max(0, item.MaxSewn));
            return Mathf.Max(0, item.BasePockets + extra);
        }

        public static int ComputeOutfitPockets(AvatarAppearance look, ClothingWardrobe wardrobe)
        {
            look?.EnsureClothingDefaults();
            var shirt = Get(look != null ? look.shirtId : DefaultShirt);
            var bottoms = Get(look != null ? look.bottomsId : DefaultBottoms);
            var n = PocketsOnItem(shirt, wardrobe) + PocketsOnItem(bottoms, wardrobe);
            return Mathf.Clamp(n, 1, MaxOutfitPockets);
        }

        public static string SlotLabel(ClothingSlot slot) => slot switch
        {
            ClothingSlot.Hat => "Hat",
            ClothingSlot.Facial => "Facial",
            ClothingSlot.Shirt => "Shirt",
            ClothingSlot.Hands => "Hands",
            ClothingSlot.Bottoms => "Bottoms",
            ClothingSlot.Shoes => "Shoes",
            ClothingSlot.Neck => "Neck",
            _ => "Slot"
        };

        public static void GrantStarter(ClothingWardrobe wardrobe)
        {
            wardrobe ??= new ClothingWardrobe();
            wardrobe.EnsureValid();
            for (var i = 0; i < All.Length; i++)
            {
                if (All[i].StarterOwned)
                    wardrobe.Grant(All[i].Id);
            }
        }

        public static void GrantAll(ClothingWardrobe wardrobe)
        {
            wardrobe ??= new ClothingWardrobe();
            wardrobe.EnsureValid();
            for (var i = 0; i < All.Length; i++)
            {
                wardrobe.Grant(All[i].Id);
                if (All[i].MaxSewn > 0)
                    wardrobe.SetSewnExtra(All[i].Id, All[i].MaxSewn);
            }
        }
    }
}
