using System;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// How the AI opponent duelist looks (ArOpponentDuelist). Named anime duelists from
    /// StreamingAssets/Decks/opponent_roster.json get their own hairstyle and outfit colours; everyone else
    /// (street wanderers, Tear bosses, unnamed practice) gets a stable look seeded from their name, built from
    /// the same customization catalogue the player uses (AVATAR_3D_INTEGRATION.md, Customization v1).
    /// </summary>
    public static class AiDuelistLooks
    {
        /// <summary>Opponents that are not a human duelist (Mr. Referobot duels from the sideline as himself).</summary>
        public static bool IsHumanDuelist(string opponentName) =>
            string.IsNullOrEmpty(opponentName) || opponentName.IndexOf("referobot", StringComparison.OrdinalIgnoreCase) < 0;

        /// <summary>Canon duelists are drawn as animated silhouettes until their real character models exist.</summary>
        public static bool CanonSilhouettes = true;

        public static AvatarAppearance For(string opponentName)
        {
            // one of our random street duelists: its name is its identity (NpcDuelistRoster)
            if (NpcDuelistRoster.TryGet(opponentName, out var npc)) return NpcDuelistRoster.LookFor(npc);

            var n = (opponentName ?? "").ToLowerInvariant();
            AvatarAppearance a = null;
            if (n.Contains("yami yugi")) a = Look(0, "yugi_muto", 158, 0.38f, 0, ("Shirt.0", "#1C2E7A"), ("Shirt.1", "#2A2A33"), ("Bottoms.0", "#1C2E7A"));
            else if (n.Contains("yugi")) a = Look(0, "yugi_muto", 153, 0.40f, 0, ("Shirt.0", "#1C2E7A"), ("Shirt.1", "#2A2A33"), ("Bottoms.0", "#1C2E7A"));
            else if (n.Contains("yami bakura")) { var _l = Look(0, "ryou_bakura", 176, 0.35f, 0, ("Shirt.0", "#3A7BFF"), ("Shirt.1", "#F2F2F5"), ("Bottoms.0", "#2A2A33")); _l.WithSkin("#404858"); a = _l; }
            else if (n.Contains("bakura")) a = Look(0, "ryou_bakura", 176, 0.35f, 0, ("Shirt.0", "#3A7BFF"), ("Shirt.1", "#F2F2F5"), ("Bottoms.0", "#2A2A33"));
            else if (n.Contains("joey")) a = Look(0, "joey_wheeler", 178, 0.50f, 0, ("Shirt.0", "#1E6A2A"), ("Shirt.1", "#F2F2F5"), ("Bottoms.0", "#1C2E7A"));
            else if (n.Contains("mai")) a = Look(1, "mai_valentine", 170, 0.45f, 0, ("Shirt.0", "#43207A"), ("Shirt.1", "#7A4CD9"), ("Bottoms.0", "#1E0E3A"));
            else if (n.Contains("weevil")) a = Look(0, "seto_kaiba", 152, 0.30f, 1, ("Shirt.0", "#F2F2F5"), ("Shirt.1", "#1E6A2A"), ("Bottoms.0", "#2A2A33")).WithHair("#2E7A6A");
            else if (n.Contains("rex")) a = Look(0, "jaden_yuki", 170, 0.55f, 1, ("Shirt.0", "#C87028"), ("Shirt.1", "#3A1E08"), ("Bottoms.0", "#4A3020")).WithHair("#5A2A6A");
            else if (n.Contains("mako")) a = Look(0, "jack_atlas", 183, 0.70f, 1, ("Shirt.0", "#0F5A6A"), ("Shirt.1", "#F2F2F5"), ("Bottoms.0", "#1C2E7A")).WithHair("#1C2470");
            else if (n.Contains("pegasus")) a = Look(0, "ryou_bakura", 190, 0.45f, 1, ("Shirt.0", "#C0303A"), ("Shirt.1", "#F2F2F5"), ("Shirt.2", "#E3B64C"), ("Bottoms.0", "#F2F2F5")).WithHair("#DAD7EA");
            else if (n.Contains("marik")) { var _l = Look(0, "marik_ishtar", 177, 0.40f, 0, ("Shirt.0", "#43207A"), ("Shirt.1", "#E3B64C"), ("Bottoms.0", "#0B0B10")); _l.WithSkin("#A8704A"); a = _l; }
            else if (n.Contains("keith")) a = Look(0, "jack_atlas", 185, 0.60f, 1, ("Shirt.0", "#2A2A33"), ("Shirt.1", "#C0303A"), ("Bottoms.0", "#1C2E7A")).WithHair("#8B4513");
            else if (n.Contains("bonz")) a = Look(0, "marik_ishtar", 172, 0.25f, 1, ("Shirt.0", "#55565E"), ("Bottoms.0", "#2A2A33")).WithHair("#8A8C94");
            else if (n == "tea" || n.StartsWith("tea ") || n.Contains("téa") || n.Contains("gardner")) a = Look(1, "tea_gardner", 164, 0.40f, 0, ("Shirt.0", "#FF9CD8"), ("Shirt.1", "#F2F2F5"), ("Bottoms.0", "#1C2E7A"));
            // the Paradox Brothers: bald, robed; Para in red, Dox in blue
            else if (n == "para" || n.StartsWith("para ")) a = Look(0, "none", 170, 0.55f, 1, ("Shirt.0", "#C8302A"), ("Shirt.1", "#E3B64C"), ("Bottoms.0", "#0B0B10"));
            else if (n == "dox" || n.StartsWith("dox ")) a = Look(0, "none", 170, 0.55f, 1, ("Shirt.0", "#2F5FD0"), ("Shirt.1", "#E3B64C"), ("Bottoms.0", "#0B0B10"));
            else if (n.Contains("paradox")) a = Look(0, "none", 170, 0.55f, 1, ("Shirt.0", "#C8302A"), ("Shirt.1", "#E3B64C"), ("Bottoms.0", "#0B0B10"));
            var canon = a != null;
            a ??= Seeded(opponentName);
            var slots = CharacterLooks.Match(opponentName);
            if (slots != null)
            {
                // Signature slot sets are on the shared body. ApplyTo clears the one-mesh outfit
                // so Yugi and Kaiba wear the split pieces instead of the old full outfit.
                slots.ApplyTo(a);
            }
            else
            {
                if (canon && CanonSilhouettes)
                {
                    // Paradox Brothers (and any named duelist still without a slot set) stay a flat silhouette.
                    a.silhouette = true;
                    a.silhouetteHex = n.Contains("kaiba") ? "#0E1A3A" : n.Contains("yugi") ? "#241238" : n.Contains("joey") ? "#0F2A1A"
                        : n.Contains("mai") ? "#2A1240" : n.Contains("marik") ? "#2A2210" : n.Contains("bakura") ? "#101828"
                        : n.Contains("pegasus") ? "#2A1018" : n.StartsWith("para") ? "#301416" : n.StartsWith("dox") ? "#14203A" : "#12162A";
                }
                foreach (var o in AvatarCatalog.Outfits)
                    if (!string.IsNullOrEmpty(o.character) && n.Contains(o.character) && o.FitsBody(a.IsFemale)) { a.outfitId = o.id; break; }
            }
            a.ClampToCatalog();
            return a;
        }

        static AvatarAppearance Look(int body, string hairId, float heightCm, float weight, int pattern,
            params (string key, string hex)[] paints)
        {
            var a = AvatarAppearance.Default();
            a.bodyType = body;
            a.hairStyleId = hairId;
            a.heightCm = heightCm;
            a.weight = weight;
            a.hairPattern = pattern;   // 0 = the style's own canon colours; 1 = solid recolour (WithHair)
            foreach (var (key, hex) in paints) a.SetPaint(key, hex);
            return a;
        }

        static AvatarAppearance WithHair(this AvatarAppearance a, string hex) { a.hairHex = hex; return a; }
        static AvatarAppearance WithSkin(this AvatarAppearance a, string hex) { a.SetPaint("Body.0", hex); a.skinHex = hex; return a; }
        static AvatarAppearance WithFace(this AvatarAppearance a, string id) { a.faceId = id; return a; }

        /// <summary>A stable random look per name (the same street duelist always looks the same).</summary>
        public static AvatarAppearance Seeded(string name)
        {
            var seed = 17;
            foreach (var c in name ?? "") seed = seed * 31 + c;
            var rng = new System.Random(seed);
            var a = AvatarAppearance.Default();
            a.bodyType = rng.Next(2);
            var fem = a.bodyType == 1;
            a.heightCm = Mathf.Round(Mathf.Lerp(AvatarCatalog.HeightMinCm(fem) + 8f, AvatarCatalog.HeightMaxCm(fem) - 10f, (float)rng.NextDouble()));
            a.weight = Mathf.Lerp(0.2f, 0.8f, (float)rng.NextDouble());
            var styles = AvatarCatalog.HairStyles;
            if (styles.Count > 0) a.hairStyleId = styles[rng.Next(styles.Count)].id;
            var pals = AvatarCatalog.HairPalettes;
            if (pals.Count > 0 && rng.NextDouble() < 0.6) pals[rng.Next(pals.Count)].ApplyTo(a);
            var sw = AvatarCatalog.ClothingSwatches;
            a.SetPaint("Shirt.0", sw[rng.Next(sw.Length)]);
            if (rng.NextDouble() < 0.6) a.SetPaint("Shirt.1", sw[rng.Next(sw.Length)]);
            a.SetPaint("Bottoms.0", sw[rng.Next(Mathf.Min(10, sw.Length))]);   // mostly dark / neutral pants
            if (rng.NextDouble() < 0.4) a.SetPaint("Shoes.0", sw[rng.Next(sw.Length)]);
            var skins = AvatarCatalog.SkinPresets;
            var sk = skins[rng.Next(skins.Length)];
            a.SetPaint("Body.0", sk);
            a.skinHex = sk;
            return a;
        }
    }
}
