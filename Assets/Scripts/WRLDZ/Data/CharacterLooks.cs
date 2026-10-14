using System;

namespace WRLDZ.Data
{
    /// <summary>
    /// One signature Duel Monsters look: hair plus the six clothing slots.
    /// Yugi and Kaiba use the sculpted outfits split into slots. The other looks are shells
    /// built on the same bodies (no Meshy spend). Para and Dox reuse generic plain clothes
    /// with the measured robe colour (Para red, Dox blue — DK anime frames) plus the robe's gold trim. Yami Yugi wears Yugi's Domino cape (the black tank and blazer),
    /// not a flat navy wash. Yami Bakura reuses Ryou's clothes (the design record: both personalities wear the
    /// same clothes) but draws his own face decal and a DK-frame-measured skin — not the old near-black blue.
    /// Borrowed hair meshes, until those heads exist: Odion and PaniK use Kaiba; Rex uses Jaden;
    /// Mako and Keith use Jack; Bonz uses Marik; Ishizu uses Mai; Duke uses Joey.
    /// Weevil, Tristan, Mokuba, and Pegasus wear their own meshes
    /// (weevil_bowl, tristan_quiff, mokuba_kaiba, pegasus_mane). PaniK wears Odion's hood so the borrowed Kaiba hair is hidden.
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
        public string HairTipHex;
        public string HairFringeHex;
        public string HairStreakHex;
        public string SkinHex;
        public string HatId;
        public string FacialId;
        public string ShirtId;
        public string HandsId;
        public string BottomsId;
        public string ShoesId;
        public string ShirtHex;
        /// <summary>Paint for the shirt's trim half (mask .1 = collar, cuffs, hem bands). Empty = same as ShirtHex.</summary>
        public string ShirtTrimHex;
        public string BottomsHex;
        public string ShoesHex;

        /// <summary>
        /// If set, loads a full character FBX from Resources/WRLDZ/Avatar3D/Characters/{characterId}
        /// instead of building the modular avatar from grafted parts. The model's own textures and
        /// mesh are used; animation retargets via the Humanoid avatar. Falls back to modular if the
        /// asset is missing.
        /// </summary>
        public string CharacterId;

        public bool FitsBody(bool female) => BodyType < 0 || BodyType == (female ? 1 : 0);

        public void ApplyTo(AvatarAppearance a)
        {
            if (a == null) return;
            a.outfitId = "";
            a.characterId = CharacterId; // full character model takes precedence over modular parts when present
            a.silhouette = false;
            // A signature set is its own painted albedo: drop any garment paints carried over from the previous look
            // (AiDuelistLooks sets generic coat colours before this runs; while the Clothes/ masks imported broken they
            // barely applied, with correct linear masks they turned Yugi's set flat navy and Kaiba's coat tan).
            foreach (var slot in new[] { "Shirt", "Bottoms", "Shoes", "Outfit" })
                for (var k = 0; k < 3; k++) a.SetPaint(slot + "." + k, "");
            if (!string.IsNullOrEmpty(ShirtHex))
                for (int k = 0; k < 3; k++) a.SetPaint("Shirt." + k, ShirtHex);
            // the flat shells split into a main half (.0) and a trim half (.1); a robe's gold collar/cuff/hem bands
            if (!string.IsNullOrEmpty(ShirtTrimHex)) a.SetPaint("Shirt.1", ShirtTrimHex);
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
            if (!string.IsNullOrEmpty(HairTipHex)) a.hairTipHex = HairTipHex;
            if (!string.IsNullOrEmpty(HairFringeHex)) a.hairFringeHex = HairFringeHex;
            if (!string.IsNullOrEmpty(HairStreakHex)) a.hairStreakHex = HairStreakHex;
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
            // Duelist Kingdom first. The DK story (season1_dk) is the live campaign, so the *_dk rows sit
            // ahead of their Battle City counterparts: Match() keeps the first row on an equal-length match,
            // so a plain "yugi"/"kaiba" resolves to the DK set while the BC set stays equippable.
            L("yugi_dk", "Duelist Kingdom Yugi", "yugi", 0, "yugi_muto", "yugi_muto", 153f, 0.40f, "shirt_yugi_dk", "bottoms_yugi_dk", "shoes_yugi_dk", "hands_yugi_dk", characterId: "yugi"),
            L("kaiba_dk", "Duelist Kingdom Kaiba", "kaiba", 0, "seto_kaiba", "seto_kaiba", 186f, 0.35f, "shirt_kaiba_dk", "bottoms_kaiba_dk", "shoes_kaiba_dk", "hands_kaiba_dk"),
            L("yugi_bc", "Battle City Yugi", "yugi", 0, "yugi_muto", "yugi_muto", 153f, 0.40f, "shirt_yugi_bc", "bottoms_yugi_bc", "shoes_yugi_bc", null, characterId: "yugi"),
            L("kaiba_bc", "Battle City Kaiba", "kaiba", 0, "seto_kaiba", "seto_kaiba", 186f, 0.35f, "shirt_kaiba_bc", "bottoms_kaiba_bc", "shoes_kaiba_bc", null),
            L("joey_bc", "Battle City Joey", "joey", 0, "joey_wheeler", "joey_wheeler", 178f, 0.50f, "shirt_joey_bc", "bottoms_joey_bc", "shoes_joey_bc", null),
            L("mai_bc", "Battle City Mai", "mai", 1, "mai_valentine", "mai_valentine", 170f, 0.45f, "shirt_mai_bc", "bottoms_mai_bc", "shoes_mai_bc", "hands_mai_bc"),
            L("tea_bc", "Battle City Téa", "tea", 1, "tea_gardner", "tea", 164f, 0.40f, "shirt_tea_bc", "bottoms_tea_bc", "shoes_tea_bc", null),
            L("bakura_bc", "Battle City Bakura", "bakura", 0, "ryou_bakura", "bakura", 176f, 0.35f, "shirt_bakura_bc", "bottoms_bakura_bc", "shoes_bakura_bc", null),
            L("marik_bc", "Battle City Marik", "marik", 0, "marik_ishtar", "marik", 177f, 0.40f, "shirt_marik_bc", "bottoms_marik_bc", "shoes_marik_bc", null, skin: "#A8704A"),
            L("pegasus_dk", "Duelist Kingdom Pegasus", "pegasus", 0, "pegasus_mane", "pegasus", 190f, 0.45f, "shirt_pegasus_dk", "bottoms_pegasus_dk", "shoes_pegasus_dk", "hands_pegasus_dk", hairPattern: 1, hairHex: "#C4BDD0"),
            // face is the 6th arg (the decal). facial is the 13th and belongs to the glasses slot:
            // a decal filename there is a Clothes/argon_<id>_<sex> lookup, so "face_weevil_bc" resolved
            // to nothing and silently fell back to face_none. Weevil gets no facial piece.
            L("weevil_bc", "Battle City Weevil", "weevil", 0, "weevil_bowl", "weevil", 152f, 0.30f, "shirt_weevil_bc", "bottoms_weevil_bc", "shoes_weevil_bc", null, hairPattern: 1, hairHex: "#8ABAB7"),
            L("rex_bc", "Battle City Rex", "rex", 0, "jaden_yuki", "rex_raptor", 170f, 0.55f, "shirt_rex_bc", "bottoms_rex_bc", "shoes_rex_bc", null, hat: "hat_rex_bc", hairPattern: 2, hairHex: "#4D3020", hairTip: "#8D7DB3"),
            L("mako_dk", "Duelist Kingdom Mako", "mako", 0, "jack_atlas", "mako", 183f, 0.70f, "shirt_mako_bc", "bottoms_mako_dk", "shoes_mako_bc", "hands_mako_dk", hat: "hat_mako_bc", hairPattern: 4, hairHex: "#3C394C", hairStreak: "#595E8F"),
            L("mako_bc", "Battle City Mako", "mako", 0, "jack_atlas", "mako", 183f, 0.70f, "shirt_mako_bc", "bottoms_mako_bc", "shoes_mako_bc", null, hat: "hat_mako_bc", hairPattern: 4, hairHex: "#3C394C", hairStreak: "#595E8F"),
            L("keith_dk", "Duelist Kingdom Keith", "keith", 0, "jack_atlas", "bandit_keith", 185f, 0.60f, "shirt_keith_dk", "bottoms_keith_dk", "shoes_keith_dk", "hands_keith_dk", hat: "hat_keith_dk", hairPattern: 1, hairHex: "#B3AF56"),
            L("bonz_dk", "Duelist Kingdom Bonz", "bonz", 0, "marik_ishtar", "bonz", 172f, 0.25f, "shirt_bonz_dk", "bottoms_bonz_dk", "shoes_bonz_dk", "hands_bonz_dk", hat: "hat_bonz_dk", hairPattern: 1, hairHex: "#505E80"),
            L("ishizu_bc", "Battle City Ishizu", "ishizu", 1, "mai_valentine", "ishizu", 172f, 0.40f, "shirt_ishizu_bc", "bottoms_ishizu_bc", "shoes_ishizu_bc", null, hat: "hat_ishizu_bc", hairPattern: 1, hairHex: "#1A1A1C"),
            L("odion_bc", "Battle City Odion", "odion", 0, "seto_kaiba", "odion", 188f, 0.70f, "shirt_odion_bc", "bottoms_odion_bc", "shoes_odion_bc", null, hat: "hat_odion_bc", hairPattern: 1, hairHex: "#1A1A1C"),
            L("duke_bc", "Battle City Duke", "duke", 0, "joey_wheeler", "duke", 180f, 0.45f, "shirt_duke_bc", "bottoms_duke_bc", "shoes_duke_bc", null, hairPattern: 1, hairHex: "#333333"),
            L("tristan_bc", "Battle City Tristan", "tristan", 0, "tristan_quiff", "tristan", 180f, 0.60f, "shirt_tristan_bc", "bottoms_tristan_bc", "shoes_tristan_bc", null, hairPattern: 1, hairHex: "#62442D"),
            L("mokuba_bc", "Battle City Mokuba", "mokuba", 0, "mokuba_kaiba", "mokuba_kaiba", 155f, 0.35f, "shirt_mokuba_bc", "bottoms_mokuba_bc", "shoes_mokuba_bc", null, hairPattern: 4, hairHex: "#1E1E1E", hairStreak: "#4C557E"),
            L("yami_yugi_dk", "Duelist Kingdom Yami Yugi", "yami yugi", 0, "yugi_muto", "yami_yugi",
                158f, 0.38f, "shirt_yami_dk", "bottoms_yami_dk", "shoes_yugi_dk", "hands_yugi_dk", characterId: "yami_yugi"),
            // The Domino cape albedo is the black tank and blue blazer. A shirt hex flattened it to navy.
            L("yami_yugi", "Yami Yugi", "yami yugi", 0, "yugi_muto", "yami_yugi", 158f, 0.38f, "shirt_yugi_bc", "bottoms_yugi_bc", "shoes_yugi_bc", null, characterId: "yami_yugi"),
            L("yami_bakura", "Yami Bakura", "yami bakura", 0, "yami_bakura", "yami_bakura", 176f, 0.35f, "shirt_bakura_bc", "bottoms_bakura_bc", "shoes_bakura_bc", null, skin: "#CFAC8D"),
            // Hood hides the borrowed Kaiba hair. The coat is the generic duel coat, painted near-black.
            L("panik_dk", "Duelist Kingdom PaniK", "panik", 0, "seto_kaiba", "panik", 185f, 0.55f, "shirt_duelcoat", "bottoms_plain", "shoes_boots", "hands_panik_dk", hat: "hat_odion_bc", hairPattern: 1, hairHex: "#2E465C", skin: "#C4B8A8", shirtHex: "#141018"),
            L("para", "Para — Paradox Brother", "para", 0, "none", "para", 170f, 0.55f, "shirt_plain", "bottoms_plain", "shoes_plain", null, hairPattern: 1, shirtHex: "#96473E", shirtTrimHex: "#E3B64C"),
            L("dox", "Dox — Paradox Brother", "dox", 0, "none", "dox", 170f, 0.55f, "shirt_plain", "bottoms_plain", "shoes_plain", null, hairPattern: 1, shirtHex: "#576EAF", shirtTrimHex: "#E3B64C"),
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
            string shirtHex = null, string bottomsHex = null, string shoesHex = null,
            string hairTip = null, string hairFringe = null, string hairStreak = null, string shirtTrimHex = null,
            string characterId = null) =>
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
                HairTipHex = hairTip,
                HairFringeHex = hairFringe,
                HairStreakHex = hairStreak,
                SkinHex = skin,
                HatId = hat,
                FacialId = facial,
                ShirtId = shirt,
                HandsId = hands,
                BottomsId = bottoms,
                ShoesId = shoes,
                ShirtHex = shirtHex,
                ShirtTrimHex = shirtTrimHex,
                BottomsHex = bottomsHex,
                ShoesHex = shoesHex,
                CharacterId = characterId
            };
    }
}
