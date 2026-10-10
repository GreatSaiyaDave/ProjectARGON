using System;

namespace WRLDZ.Data
{
    /// <summary>
    /// One signature Duel Monsters look: hair plus the six clothing slots.
    /// Yugi and Kaiba use the sculpted outfits split into slots. The other looks are shells
    /// built on the same bodies (no Meshy spend). Para and Dox reuse generic plain clothes
    /// with a solid robe colour. Yami Yugi wears Yugi's Domino cape (the black tank and blazer),
    /// not a flat navy wash. Yami Bakura reuses Ryou's clothes with a grey skin.
    /// Borrowed hair meshes, until those heads exist: Weevil and Odion use Kaiba; Rex uses Jaden;
    /// Mako and Keith use Jack; Bonz uses Marik; Pegasus uses Ryou, tinted silver; Ishizu uses Mai;
    /// Duke and Tristan use Joey; Mokuba uses Yugi. PaniK wears Odion's hood so that borrowed hair is hidden.
    /// </summary>
    public sealed class CharacterLook
    {
        public string Id;
        public string Name;
        public string Match;
        public int BodyType;
        public string HairId;
        public string FaceId;
        public float HeightCm;
        public float Weight;
        public int HairPattern;
        public string HairHex;
        public string SkinHex;
        public string HatId;
        public string FacialId;
        public string ShirtId;
        public string HandsId;
        public string BottomsId;
        public string ShoesId;
        public string ShirtHex;
        public string BottomsHex;
        public string ShoesHex;

        public bool FitsBody(bool female) => BodyType < 0 || BodyType == (female ? 1 : 0);

        public void ApplyTo(AvatarAppearance a)
        {
            if (a == null) return;
            a.outfitId = "";
            a.characterId = "";
            a.silhouette = false;
            // A signature set is its own painted albedo: drop any garment paints carried over from the previous look
            // (AiDuelistLooks sets generic coat colours before this runs; while the Clothes/ masks imported broken they
            // barely applied, with correct linear masks they turned Yugi's set flat navy and Kaiba's coat tan).
            foreach (var slot in new[] { "Shirt", "Bottoms", "Shoes", "Outfit" })
                for (var k = 0; k < 3; k++) a.SetPaint(slot + "." + k, "");
            if (!string.IsNullOrEmpty(ShirtHex))
                for (int k = 0; k < 3; k++) a.SetPaint("Shirt." + k, ShirtHex);
            if (!string.IsNullOrEmpty(BottomsHex))
                for (int k = 0; k < 3; k++) a.SetPaint("Bottoms." + k, BottomsHex);
            if (!string.IsNullOrEmpty(ShoesHex))
                for (int k = 0; k < 3; k++) a.SetPaint("Shoes." + k, ShoesHex);
            a.bodyType = BodyType;
            a.hairStyleId = HairId ?? "";
            a.faceId = FaceId ?? "";
            a.heightCm = HeightCm;
            a.weight = Weight;
            a.hairPattern = HairPattern;
            if (!string.IsNullOrEmpty(HairHex)) a.hairHex = HairHex;
            if (!string.IsNullOrEmpty(SkinHex))
            {
                a.skinHex = SkinHex;
                a.SetPaint("Body.0", SkinHex);
            }
            else
            {
                // the previous signature look's forced skin (Marik's) must not carry over; the player's own skin paint stays
                var cur = a.GetPaint("Body.0");
                foreach (var l in CharacterLooks.All)
                    if (!string.IsNullOrEmpty(l.SkinHex) && string.Equals(cur, l.SkinHex, System.StringComparison.OrdinalIgnoreCase))
                    {
                        a.SetPaint("Body.0", "");
                        a.skinHex = new AvatarAppearance().skinHex;
                        break;
                    }
            }
            a.hatId = string.IsNullOrEmpty(HatId) ? ClothingCatalog.DefaultHat : HatId;
            a.facialId = string.IsNullOrEmpty(FacialId) ? ClothingCatalog.DefaultFacial : FacialId;
            a.shirtId = string.IsNullOrEmpty(ShirtId) ? ClothingCatalog.DefaultShirt : ShirtId;
            a.handsId = string.IsNullOrEmpty(HandsId) ? ClothingCatalog.DefaultHands : HandsId;
            a.bottomsId = string.IsNullOrEmpty(BottomsId) ? ClothingCatalog.DefaultBottoms : BottomsId;
            a.shoesId = string.IsNullOrEmpty(ShoesId) ? ClothingCatalog.DefaultShoes : ShoesId;
        }
    }

    public static class CharacterLooks
    {
        public static readonly CharacterLook[] All =
        {
            L("yugi_bc", "Battle City Yugi", "yugi", 0, "yugi_muto", "", 153f, 0.40f, "shirt_yugi_bc", "bottoms_yugi_bc", "shoes_yugi_bc", null),
            L("kaiba_bc", "Battle City Kaiba", "kaiba", 0, "seto_kaiba", "seto_kaiba", 186f, 0.35f, "shirt_kaiba_bc", "bottoms_kaiba_bc", "shoes_kaiba_bc", null),
            L("joey_bc", "Battle City Joey", "joey", 0, "joey_wheeler", "", 178f, 0.50f, "shirt_joey_bc", "bottoms_joey_bc", "shoes_joey_bc", null),
            L("mai_bc", "Battle City Mai", "mai", 1, "mai_valentine", "", 170f, 0.45f, "shirt_mai_bc", "bottoms_mai_bc", "shoes_mai_bc", "hands_mai_bc"),
            L("tea_bc", "Battle City Téa", "tea", 1, "tea_gardner", "", 164f, 0.40f, "shirt_tea_bc", "bottoms_tea_bc", "shoes_tea_bc", null),
            L("bakura_bc", "Battle City Bakura", "bakura", 0, "ryou_bakura", "", 176f, 0.35f, "shirt_bakura_bc", "bottoms_bakura_bc", "shoes_bakura_bc", null),
            L("marik_bc", "Battle City Marik", "marik", 0, "marik_ishtar", "", 177f, 0.40f, "shirt_marik_bc", "bottoms_marik_bc", "shoes_marik_bc", null, skin: "#A8704A"),
            L("pegasus_dk", "Duelist Kingdom Pegasus", "pegasus", 0, "ryou_bakura", "", 190f, 0.45f, "shirt_pegasus_dk", "bottoms_pegasus_dk", "shoes_pegasus_dk", null, hairPattern: 1, hairHex: "#DAD7EA"),
            L("weevil_bc", "Battle City Weevil", "weevil", 0, "seto_kaiba", "", 152f, 0.30f, "shirt_weevil_bc", "bottoms_weevil_bc", "shoes_weevil_bc", null, "face_weevil_bc", hairPattern: 1, hairHex: "#2E7A6A"),
            L("rex_bc", "Battle City Rex", "rex", 0, "jaden_yuki", "", 170f, 0.55f, "shirt_rex_bc", "bottoms_rex_bc", "shoes_rex_bc", null, hat: "hat_rex_bc", hairPattern: 1, hairHex: "#5A2A6A"),
            L("mako_bc", "Battle City Mako", "mako", 0, "jack_atlas", "", 183f, 0.70f, "shirt_mako_bc", "bottoms_mako_bc", "shoes_mako_bc", null, hat: "hat_mako_bc", hairPattern: 1, hairHex: "#1C2470"),
            L("keith_dk", "Duelist Kingdom Keith", "keith", 0, "jack_atlas", "", 185f, 0.60f, "shirt_keith_dk", "bottoms_keith_dk", "shoes_keith_dk", "hands_keith_dk", hat: "hat_keith_dk", hairPattern: 1, hairHex: "#8B4513"),
            L("bonz_dk", "Duelist Kingdom Bonz", "bonz", 0, "marik_ishtar", "", 172f, 0.25f, "shirt_bonz_dk", "bottoms_bonz_dk", "shoes_bonz_dk", null, hat: "hat_bonz_dk", hairPattern: 1, hairHex: "#8A8C94"),
            L("ishizu_bc", "Battle City Ishizu", "ishizu", 1, "mai_valentine", "", 172f, 0.40f, "shirt_ishizu_bc", "bottoms_ishizu_bc", "shoes_ishizu_bc", null, hat: "hat_ishizu_bc", hairPattern: 1, hairHex: "#1A1A1C"),
            L("odion_bc", "Battle City Odion", "odion", 0, "seto_kaiba", "", 188f, 0.70f, "shirt_odion_bc", "bottoms_odion_bc", "shoes_odion_bc", null, hat: "hat_odion_bc", hairPattern: 1, hairHex: "#1A1A1C"),
            L("duke_bc", "Battle City Duke", "duke", 0, "joey_wheeler", "", 180f, 0.45f, "shirt_duke_bc", "bottoms_duke_bc", "shoes_duke_bc", null, hairPattern: 1, hairHex: "#3A2A22"),
            L("tristan_bc", "Battle City Tristan", "tristan", 0, "joey_wheeler", "", 180f, 0.60f, "shirt_tristan_bc", "bottoms_tristan_bc", "shoes_tristan_bc", null),
            L("mokuba_bc", "Battle City Mokuba", "mokuba", 0, "yugi_muto", "", 155f, 0.35f, "shirt_mokuba_bc", "bottoms_mokuba_bc", "shoes_mokuba_bc", null),
            // The Domino cape albedo is the black tank and blue blazer. A shirt hex flattened it to navy.
            L("yami_yugi", "Yami Yugi", "yami yugi", 0, "yugi_muto", "", 158f, 0.38f, "shirt_yugi_bc", "bottoms_yugi_bc", "shoes_yugi_bc", null),
            L("yami_bakura", "Yami Bakura", "yami bakura", 0, "ryou_bakura", "", 176f, 0.35f, "shirt_bakura_bc", "bottoms_bakura_bc", "shoes_bakura_bc", null, skin: "#404858"),
            // Hood hides the borrowed Kaiba hair. The coat is the generic duel coat, painted near-black.
            L("panik_dk", "Duelist Kingdom PaniK", "panik", 0, "seto_kaiba", "", 185f, 0.55f, "shirt_duelcoat", "bottoms_plain", "shoes_boots", null, hat: "hat_odion_bc", hairPattern: 1, hairHex: "#1A1A1C", skin: "#C4B8A8", shirtHex: "#141018"),
            L("para", "Para — Paradox Brother", "para", 0, "none", null, 170f, 0.55f, "shirt_plain", "bottoms_plain", "shoes_plain", null, hairPattern: 1, shirtHex: "#C8302A"),
            L("dox", "Dox — Paradox Brother", "dox", 0, "none", null, 170f, 0.55f, "shirt_plain", "bottoms_plain", "shoes_plain", null, hairPattern: 1, shirtHex: "#2F5FD0"),
        };

        public static CharacterLook Match(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var n = name.ToLowerInvariant();
            if (n.Contains("referobot")) return null;
            // "tea" is a substring of other words; require a duelist-shaped token.
            if (n == "tea" || n.StartsWith("tea ") || n.Contains("téa") || n.Contains("gardner"))
                return Find("tea_bc");
            CharacterLook found = null;
            for (var i = 0; i < All.Length; i++)
            {
                var look = All[i];
                if (look.Id == "tea_bc") continue;
                if (n.Contains(look.Match) && (found == null || look.Match.Length > found.Match.Length))
                    found = look;
            }
            return found;
        }

        public static CharacterLook Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];
            return null;
        }

        /// <summary>True when the six slots are exactly this look.</summary>
        public static bool Wearing(AvatarAppearance a, CharacterLook look)
        {
            if (a == null || look == null) return false;
            return Eq(a.hatId, look.HatId, ClothingCatalog.DefaultHat)
                   && Eq(a.facialId, look.FacialId, ClothingCatalog.DefaultFacial)
                   && Eq(a.shirtId, look.ShirtId, ClothingCatalog.DefaultShirt)
                   && Eq(a.handsId, look.HandsId, ClothingCatalog.DefaultHands)
                   && Eq(a.bottomsId, look.BottomsId, ClothingCatalog.DefaultBottoms)
                   && Eq(a.shoesId, look.ShoesId, ClothingCatalog.DefaultShoes);
        }

        static bool Eq(string have, string want, string empty)
        {
            if (string.IsNullOrEmpty(want)) want = empty;
            if (string.IsNullOrEmpty(have)) have = empty;
            return have == want;
        }

        static CharacterLook L(string id, string name, string match, int body, string hair, string face,
            float height, float weight, string shirt, string bottoms, string shoes, string hands,
            string facial = null, string hat = null, int hairPattern = 0, string hairHex = null, string skin = null,
            string shirtHex = null, string bottomsHex = null, string shoesHex = null) =>
            new()
            {
                Id = id,
                Name = name,
                Match = match,
                BodyType = body,
                HairId = hair,
                FaceId = face,
                HeightCm = height,
                Weight = weight,
                HairPattern = hairPattern,
                HairHex = hairHex,
                SkinHex = skin,
                HatId = hat,
                FacialId = facial,
                ShirtId = shirt,
                HandsId = hands,
                BottomsId = bottoms,
                ShoesId = shoes,
                ShirtHex = shirtHex,
                BottomsHex = bottomsHex,
                ShoesHex = shoesHex
            };
    }
}
