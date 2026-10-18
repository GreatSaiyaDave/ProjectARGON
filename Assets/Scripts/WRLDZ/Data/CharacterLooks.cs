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
        /// <summary>Neck slot (cord, pendant, Millennium item); empty = bare.</summary>
        public string NeckId;
        /// <summary>Build / face shapes, "Build_Gaunt:1,Face_JawWide:0.5" (AvatarCatalog.BodyMorphs); empty = as built.</summary>
        public string Morphs;
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
            a.neckId = string.IsNullOrEmpty(NeckId) ? ClothingCatalog.DefaultNeck : NeckId;
            foreach (var (key, _, _) in AvatarCatalog.BodyMorphs) a.SetMorph(key, 0f);
            if (!string.IsNullOrEmpty(Morphs))
                foreach (var pair in Morphs.Split(','))
                {
                    var kv = pair.Split(':');
                    if (kv.Length == 2 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
                        a.SetMorph(kv[0].Trim(), w);
                }
        }
    }

    public static class CharacterLooks
    {
        public static readonly CharacterLook[] All =
        {
            // Duelist Kingdom first. The DK story (season1_dk) is the live campaign, so the *_dk rows sit
            // ahead of their Battle City counterparts: Match() keeps the first row on an equal-length match,
            // so a plain "yugi"/"kaiba" resolves to the DK set while the BC set stays equippable.
            //
            // DA renders (2026-10-15). The live campaign is Duelist Kingdom (season1_dk), so every character's
            // name-reachable row now points at its "*_da" sprite set: the Duelist Kingdom (Season 1) character
            // renders by GravekeepersBrewer (deviantart.com/gravekeepersbrewer). These replace the older
            // modular/official-art sets for the DK cast. Credit + pipeline: Docs/anime_variants_review/DA_RENDERS_FINDINGS.md.
            // Marik, Ishizu and Odion are Battle City only and have no DK render, so they keep their sets.
            // To revert one character to its previous look, restore its characterId, e.g.
            //   yami_yugi_dk  "yami_yugi_da"    ->  "yami_yugi"
            //   yugi_dk       "yugi_da"         ->  ""            (previous: modular avatar)
            //   kaiba_dk      "kaiba_da"        ->  "kaiba"
            //   joey_bc       "joey_da"         ->  "joey"
            //   mai_bc        "mai_da"          ->  "mai"
            //   tea_bc        "tea_da"          ->  "tea"
            //   tristan_bc    "tristan_da"      ->  "tristan"
            //   bakura_bc     "bakura_da"       ->  "bakura"       (Ryo Bakura — see note below)
            //   yami_bakura   "yami_bakura_da"  ->  "bakura"
            //   pegasus_dk    "pegasus_da"      ->  "pegasus"
            //   weevil_bc     "weevil_da"       ->  "weevil"
            //   rex_bc        "rex_da"          ->  "rex"
            //   mako_dk       "mako_da"         ->  "mako"
            //   keith_dk      "bandit_keith_da" ->  "bandit_keith"
            //   mokuba_bc     "mokuba_da"       ->  "mokuba"
            // NOTE: the old catalog "bakura" set held Yami Bakura art, so Ryo Bakura used to render as Yami
            // Bakura. Pointing bakura_bc at "bakura_da" (the DK Ryo Bakura render) also fixes that.
            // Joey and Ryo Bakura now come from the DK gallery too, so their earlier "no trustworthy source"
            // note is retired.
            // 2026-10-18: the DK rows wear the Meshy sculpts (ids *_3d, Tools/avatar/meshy_wardrobe.py) where one exists;
            // *_3d hair is painted, so those rows use HairPattern.Original (0) and keep hairHex only for the scalp strip.
            // The 2D sprite (characterId) still wins while it is set; clear it to see the 3D body in these pieces.
            L("yugi_dk", "Duelist Kingdom Yugi", "yugi", 0, "yugi_muto", "yugi_muto", 153f, 0.40f, "shirt_yugi_3d", "bottoms_yugi_3d", "shoes_yugi_3d", "hands_yugi_dk", characterId: "yugi_da", neck: "neck_yugi_3d"),
            L("kaiba_dk", "Duelist Kingdom Kaiba", "kaiba", 0, "seto_kaiba", "seto_kaiba", 186f, 0.35f, "shirt_kaiba_3d", "bottoms_kaiba_dk", "shoes_kaiba_3d", "hands_kaiba_dk", characterId: "kaiba_da"),
            L("yugi_bc", "Battle City Yugi", "yugi", 0, "yugi_muto", "yugi_muto", 153f, 0.40f, "shirt_yugi_bc", "bottoms_yugi_bc", "shoes_yugi_bc", null, characterId: ""),
            L("kaiba_bc", "Battle City Kaiba", "kaiba", 0, "seto_kaiba", "seto_kaiba", 186f, 0.35f, "shirt_kaiba_bc", "bottoms_kaiba_bc", "shoes_kaiba_bc", null, characterId: "kaiba"),
            L("joey_dk", "Duelist Kingdom Joey", "joey", 0, "joey_wheeler_3d", "joey_wheeler", 178f, 0.50f, "shirt_joey_3d", "bottoms_joey_bc", "shoes_joey_3d", "hands_joey_dk", characterId: "joey_da"),
            L("joey_bc", "Battle City Joey", "joey", 0, "joey_wheeler", "joey_wheeler", 178f, 0.50f, "shirt_joey_bc", "bottoms_joey_bc", "shoes_joey_bc", null, characterId: "joey_da"),
            L("mai_dk", "Duelist Kingdom Mai", "mai", 1, "mai_valentine_3d", "mai_valentine", 170f, 0.45f, "shirt_mai_3d", "bottoms_mai_3d", "shoes_mai_3d", "hands_mai_dk", characterId: "mai_da"),
            L("mai_bc", "Battle City Mai", "mai", 1, "mai_valentine", "mai_valentine", 170f, 0.45f, "shirt_mai_bc", "bottoms_mai_bc", "shoes_mai_bc", "hands_mai_bc", characterId: "mai_da"),
            L("tea_bc", "Battle City Téa", "tea", 1, "tea_gardner", "tea", 164f, 0.40f, "shirt_tea_bc", "bottoms_tea_bc", "shoes_tea_bc", null, characterId: "tea_da"),
            L("bakura_bc", "Battle City Bakura", "bakura", 0, "ryou_bakura", "bakura", 176f, 0.35f, "shirt_bakura_bc", "bottoms_bakura_bc", "shoes_bakura_bc", null, characterId: "bakura_da"),
            L("marik_bc", "Battle City Marik", "marik", 0, "marik_ishtar", "marik", 177f, 0.40f, "shirt_marik_bc", "bottoms_marik_bc", "shoes_marik_bc", null, skin: "#A8704A", characterId: "marik"),
            L("pegasus_dk", "Duelist Kingdom Pegasus", "pegasus", 0, "pegasus_mane_3d", "pegasus", 190f, 0.45f, "shirt_pegasus_3d", "bottoms_pegasus_3d", "shoes_pegasus_3d", "hands_pegasus_dk", hairHex: "#C4BDD0", characterId: "pegasus_da"),
            // face is the 6th arg (the decal). facial is the 13th and belongs to the glasses slot:
            // a decal filename there is a Clothes/argon_<id>_<sex> lookup, so "face_weevil_bc" resolved
            // to nothing and silently fell back to face_none. Weevil gets no facial piece.
            L("weevil_dk", "Duelist Kingdom Weevil", "weevil", 0, "weevil_bowl_3d", "weevil", 152f, 0.30f, "shirt_weevil_3d", "bottoms_weevil_3d", "shoes_weevil_bc", null, facial: "face_weevil_3d", hairHex: "#8ABAB7", characterId: "weevil_da"),
            L("weevil_bc", "Battle City Weevil", "weevil", 0, "weevil_bowl", "weevil", 152f, 0.30f, "shirt_weevil_bc", "bottoms_weevil_bc", "shoes_weevil_bc", null, hairPattern: 1, hairHex: "#8ABAB7", characterId: "weevil_da"),
            L("rex_dk", "Duelist Kingdom Rex", "rex", 0, "jaden_yuki", "rex_raptor", 170f, 0.55f, "shirt_rex_bc", "bottoms_rex_bc", "shoes_rex_3d", null, hat: "hat_rex_bc", hairPattern: 2, hairHex: "#4D3020", hairTip: "#8D7DB3", characterId: "rex_da"),
            L("rex_bc", "Battle City Rex", "rex", 0, "jaden_yuki", "rex_raptor", 170f, 0.55f, "shirt_rex_bc", "bottoms_rex_bc", "shoes_rex_bc", null, hat: "hat_rex_bc", hairPattern: 2, hairHex: "#4D3020", hairTip: "#8D7DB3", characterId: "rex_da"),
            L("mako_dk", "Duelist Kingdom Mako", "mako", 0, "mako_3d", "mako", 183f, 0.70f, "shirt_mako_bc", "bottoms_mako_3d", "shoes_mako_3d", "hands_mako_dk", hat: "hat_mako_bc", hairHex: "#3C394C", characterId: "mako_da"),
            L("mako_bc", "Battle City Mako", "mako", 0, "jack_atlas", "mako", 183f, 0.70f, "shirt_mako_bc", "bottoms_mako_bc", "shoes_mako_bc", null, hat: "hat_mako_bc", hairPattern: 4, hairHex: "#3C394C", hairStreak: "#595E8F", characterId: "mako"),
            L("keith_dk", "Duelist Kingdom Keith", "keith", 0, "jack_atlas", "bandit_keith", 185f, 0.60f, "shirt_keith_3d", "bottoms_keith_3d", "shoes_keith_3d", "hands_keith_dk", hat: "hat_keith_3d", hairPattern: 1, hairHex: "#B3AF56", characterId: "bandit_keith_da"),
            // Bonz: 3 ft and gaunt per the owner (2026-10-18); the stature rule keeps him big-headed at 91 cm
            L("bonz_dk", "Duelist Kingdom Bonz", "bonz", 0, "bonz_3d", "bonz", 91f, 0.2f, "shirt_bonz_3d", "bottoms_bonz_3d", "shoes_bonz_3d", "hands_bonz_dk", hairHex: "#505E80", morphs: "Build_Gaunt:1"),
            L("ishizu_bc", "Battle City Ishizu", "ishizu", 1, "mai_valentine", "ishizu", 172f, 0.40f, "shirt_ishizu_bc", "bottoms_ishizu_bc", "shoes_ishizu_bc", null, hat: "hat_ishizu_bc", hairPattern: 1, hairHex: "#1A1A1C", characterId: "ishizu"),
            L("odion_bc", "Battle City Odion", "odion", 0, "seto_kaiba", "odion", 188f, 0.70f, "shirt_odion_bc", "bottoms_odion_bc", "shoes_odion_bc", null, hat: "hat_odion_bc", hairPattern: 1, hairHex: "#1A1A1C", characterId: "odion"),
            L("duke_bc", "Battle City Duke", "duke", 0, "joey_wheeler", "duke", 180f, 0.45f, "shirt_duke_bc", "bottoms_duke_bc", "shoes_duke_bc", null, hairPattern: 1, hairHex: "#333333"),
            L("tristan_bc", "Battle City Tristan", "tristan", 0, "tristan_quiff", "tristan", 180f, 0.60f, "shirt_tristan_bc", "bottoms_tristan_bc", "shoes_tristan_bc", null, hairPattern: 1, hairHex: "#62442D", characterId: "tristan_da"),
            L("mokuba_dk", "Duelist Kingdom Mokuba", "mokuba", 0, "mokuba_kaiba_3d", "mokuba_kaiba", 155f, 0.35f, "shirt_mokuba_3d", "bottoms_mokuba_3d", "shoes_mokuba_3d", null, hairHex: "#1E1E1E", characterId: "mokuba_da"),
            L("mokuba_bc", "Battle City Mokuba", "mokuba", 0, "mokuba_kaiba", "mokuba_kaiba", 155f, 0.35f, "shirt_mokuba_bc", "bottoms_mokuba_bc", "shoes_mokuba_bc", null, hairPattern: 4, hairHex: "#1E1E1E", hairStreak: "#4C557E", characterId: "mokuba_da"),
            L("yami_yugi_dk", "Duelist Kingdom Yami Yugi", "yami yugi", 0, "yugi_muto", "yami_yugi",
                158f, 0.38f, "shirt_yugi_3d", "bottoms_yugi_3d", "shoes_yugi_3d", "hands_yugi_dk", characterId: "yami_yugi_da", neck: "neck_yugi_3d"),
            // The Domino cape albedo is the black tank and blue blazer. A shirt hex flattened it to navy.
            L("yami_yugi", "Yami Yugi", "yami yugi", 0, "yugi_muto", "yami_yugi", 158f, 0.38f, "shirt_yugi_bc", "bottoms_yugi_bc", "shoes_yugi_bc", null, characterId: "yami_yugi"),
            L("yami_bakura", "Yami Bakura", "yami bakura", 0, "yami_bakura", "yami_bakura", 176f, 0.35f, "shirt_bakura_bc", "bottoms_bakura_bc", "shoes_bakura_bc", null, skin: "#CFAC8D", characterId: "yami_bakura_da", neck: "neck_mring_3d"),
            // PaniK's cap carries his winged hair (hides the borrowed Kaiba style); coat, trousers and boots are the sculpts.
            L("panik_dk", "Duelist Kingdom PaniK", "panik", 0, "seto_kaiba", "panik", 185f, 0.55f, "shirt_panik_3d", "bottoms_panik_3d", "shoes_panik_3d", "hands_panik_dk", hat: "hat_panik_3d", hairPattern: 1, hairHex: "#2E465C", skin: "#C4B8A8"),
            // The Paradox Brothers wear their sculpted heads (the face is on the head preset, so no decal) and tunics.
            L("para", "Para — Paradox Brother", "para", 0, "none", "", 170f, 0.55f, "shirt_para_3d", "bottoms_plain", "shoes_plain", null, hat: "hat_para_head_3d", hairPattern: 1),
            L("dox", "Dox — Paradox Brother", "dox", 0, "none", "", 170f, 0.55f, "shirt_dox_3d", "bottoms_plain", "shoes_plain", null, hat: "hat_dox_head_3d", hairPattern: 1),
            // Shadi (not a duelist; the Millennium Key hangs at his chest). Height not sourced: base height.
            L("shadi", "Shadi", "shadi", 0, "none", "", 175f, 0.45f, "shirt_shadi_3d", "bottoms_plain", "shoes_shadi_3d", null, hat: "hat_shadi_3d", hairPattern: 1, bottomsHex: "#F2F0EA", neck: "neck_mkey_3d"),
            // ---- Variant looks: selectable outfit variants for NPC opponents. ----
            // These rows set Match to their own Id (e.g. "yugi_school"), so name-based Match() never
            // picks them — they're only used when a roster entry sets characterLookId explicitly.
            // School uniforms drop the star-chip glove; the unbuttoned variant uses a plain tee for
            // Joey's sleeveless-vest look; the glowing-mark variant uses Yami's face decal.
            L("yugi_school", "School Uniform Yugi", "yugi_school", 0, "yugi_muto", "yugi_muto", 153f, 0.40f, "shirt_yugi_3d", "bottoms_yugi_3d", "shoes_yugi_3d", null, characterId: ""),
            L("kaiba_school", "School Uniform Kaiba", "kaiba_school", 0, "seto_kaiba", "seto_kaiba", 186f, 0.35f, "shirt_kaiba_dk", "bottoms_kaiba_dk", "shoes_kaiba_dk", null),
            L("joey_school", "School Uniform Joey", "joey_school", 0, "joey_wheeler_3d", "joey_wheeler", 178f, 0.50f, "shirt_yugi_3d", "bottoms_yugi_3d", "shoes_yugi_3d", null),
            L("joey_unbuttoned", "Joey Wheeler — Unbuttoned", "joey_unbuttoned", 0, "joey_wheeler", "joey_wheeler", 178f, 0.50f, "shirt_plain", "bottoms_joey_bc", "shoes_joey_bc", null, shirtHex: "#FF8C1A", shirtTrimHex: "#8B0000"),
            L("yami_yugi_glowing", "Yami Yugi — Glowing Mark", "yami_yugi_glowing", 0, "yugi_muto", "yami_yugi", 158f, 0.38f, "shirt_yugi_bc", "bottoms_yugi_bc", "shoes_yugi_bc", null, characterId: "yami_yugi"),
            L("bakura_dk", "Duelist Kingdom Bakura", "bakura_dk", 0, "ryou_bakura_3d", "bakura", 176f, 0.35f, "shirt_bakura_3d", "bottoms_bakura_3d", "shoes_bakura_3d", null, hairHex: "#BAB4C6"),
            L("yami_bakura_dk", "Duelist Kingdom Yami Bakura", "yami_bakura_dk", 0, "yami_bakura_3d", "yami_bakura", 176f, 0.35f, "shirt_bakura_3d", "bottoms_bakura_3d", "shoes_bakura_3d", null, hairHex: "#1A1A1C", skin: "#CFAC8D", characterId: "yami_bakura_da", neck: "neck_mring_3d"),
            L("tristan_dk", "Duelist Kingdom Tristan", "tristan_dk", 0, "tristan_quiff_3d", "tristan", 180f, 0.60f, "shirt_plain", "bottoms_plain", "shoes_plain", null, hairHex: "#62442D", shirtHex: "#2F7A2F", shirtTrimHex: "#E8C880", characterId: "tristan_da"),
            L("tea_dk", "Duelist Kingdom Téa", "tea_dk", 1, "tea_gardner_3d", "tea", 164f, 0.40f, "shirt_plain", "bottoms_plain", "shoes_plain", null, shirtHex: "#4A6FA0", shirtTrimHex: "#FFFFFF", characterId: "tea_da"),
            // Domino High uniform (the Meshy "tea_uniform" sculpts): a variant, not her Duelist Kingdom outfit.
            L("tea_school", "School Uniform Téa", "tea_school", 1, "tea_gardner_3d", "tea", 164f, 0.40f, "shirt_tea_3d", "bottoms_tea_3d", "shoes_tea_3d", null, characterId: ""),
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
                   && Eq(a.shoesId, look.ShoesId, ClothingCatalog.DefaultShoes)
                   && Eq(a.neckId, look.NeckId, ClothingCatalog.DefaultNeck);
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
            string characterId = null, string neck = null, string morphs = null) =>
            new()
            {
                NeckId = neck,
                Morphs = morphs,
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
