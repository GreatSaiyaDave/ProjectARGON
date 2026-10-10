using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Data
{
    /// <summary>
    /// The random street duelists: NPC variants built from our own avatar pipeline (base bodies, catalogue hairstyles,
    /// garment paints), so every one of them shares one art style with the player. A duelist's identity is its NAME:
    /// everything else (archetype, body, hair, colours, decks) derives from a stable hash of the name, so the overworld,
    /// the Vs AI list and the AR duel all rebuild the same person from the name alone.
    /// </summary>
    public static class NpcDuelistRoster
    {
        public sealed class Archetype
        {
            public string Id, Label;
            public int Sex = -1;                                   // -1 either, 0 male, 1 female
            public Vector2 HeightCm = new(160f, 185f);
            public Vector2 Weight = new(0.3f, 0.7f);
            public string[] Hair = Array.Empty<string>();         // catalogue style ids (empty = any of the catalogue)
            public string[] HairHex = { "#1B1B22", "#3A2415", "#6B4526", "#B0652A" };
            public string[] Shirt = { "#2F5FD0" }, Shirt2 = { "#F2F2F5" }, Bottoms = { "#1C2340" }, Shoes = { "#111318" };
            public string[] Decks4000 = Array.Empty<string>();     // wander_4000_*.json
            public string[] Decks8000 = Array.Empty<string>();     // wander_8000_*.json
            public float HardShare = 0.35f;                        // chance this one duels at 8000 LP
        }

        public sealed class NpcDuelist
        {
            public string Name, ArchetypeId, ArchetypeLabel, Deck4000, Deck8000;
            public int Sex;
            public StreetLpBand Band;
            public int Seed;
            public string Title => $"{Name} · {ArchetypeLabel}";
            public string DeckFor(StreetLpBand band) => band == StreetLpBand.Street8000 ? Deck8000 : Deck4000;
        }

        static readonly string[] FirstMale = { "Kenji", "Marcus", "Ryo", "Devon", "Hiro", "Tariq", "Leon", "Sota", "Andre", "Yuki", "Dmitri", "Kofi", "Ren", "Mateo", "Jun", "Elias", "Kazuo", "Malik", "Theo", "Daichi" };
        static readonly string[] FirstFemale = { "Rika", "Dana", "Aiko", "Priya", "Mio", "Nadia", "Sora", "Imani", "Hana", "Lena", "Yumi", "Zara", "Kaori", "Bea", "Mei", "Talia", "Emi", "Noor", "Rin", "Sasha" };
        static readonly string[] Last = { "Sato", "Reyes", "Okafor", "Tanaka", "Novak", "Ito", "Brennan", "Suzuki", "Haddad", "Kimura", "Silva", "Moreau", "Yamada", "Osei", "Nakamura", "Petrov", "Watanabe", "Cruz", "Hayashi", "Lindqvist" };

        static readonly Archetype[] Archetypes =
        {
            new() { Id = "corner_kid", Label = "Corner Kid", HeightCm = new(150f, 168f), Weight = new(0.25f, 0.55f), Hair = new[] { "storm_crown", "jaden_yuki", "yuma_tsukumo" }, Shirt = new[] { "#E43D3D", "#F2A83B", "#2F9E6B" }, Shirt2 = new[] { "#F2F2F5" }, Bottoms = new[] { "#1C2340", "#3A3A44" }, Decks4000 = new[] { "wander_4000_beatdown.json", "wander_4000_play_redeyes.json", "wander_4000_play_gaia.json" }, Decks8000 = new[] { "wander_8000_red_eyes.json" }, HardShare = 0.15f },
            new() { Id = "punk", Label = "Street Punk", HeightCm = new(168f, 186f), Weight = new(0.35f, 0.7f), Hair = new[] { "crimson_flame", "jack_atlas", "akiza_izinski" }, HairHex = new[] { "#C8302A", "#7A4CD9", "#1B1B22", "#E8B84A" }, Shirt = new[] { "#111318", "#5A1E8A" }, Shirt2 = new[] { "#C8302A", "#E8B84A" }, Bottoms = new[] { "#0B0B10", "#2A2A33" }, Shoes = new[] { "#C8302A", "#111318" }, Decks4000 = new[] { "wander_4000_zombie.json", "wander_4000_beatdown.json" }, Decks8000 = new[] { "wander_8000_red_eyes.json", "wander_8000_gravity.json" }, HardShare = 0.5f },
            new() { Id = "schoolgirl", Label = "Schoolyard Duelist", Sex = 1, HeightCm = new(152f, 168f), Weight = new(0.2f, 0.5f), Hair = new[] { "alexis_rhodes", "tea_gardner", "akiza_izinski" }, Shirt = new[] { "#F2F2F5", "#FF9CD8" }, Shirt2 = new[] { "#1C2E7A", "#C8302A" }, Bottoms = new[] { "#1C2E7A", "#3A3A44" }, Decks4000 = new[] { "wander_4000_play_harpie.json", "wander_4000_play_thunder.json", "wander_4000_play_searchers.json" }, Decks8000 = new[] { "wander_8000_harpie.json", "wander_8000_amazoness.json" }, HardShare = 0.3f },
            new() { Id = "salaryman", Label = "Quiet Commuter", Sex = 0, HeightCm = new(168f, 182f), Weight = new(0.4f, 0.8f), Hair = new[] { "storm_crown", "ryou_bakura" }, HairHex = new[] { "#1B1B22", "#3A2415", "#55565E" }, Shirt = new[] { "#F2F2F5", "#8FB4E8" }, Shirt2 = new[] { "#2A2A33" }, Bottoms = new[] { "#2A2A33", "#0B0B10" }, Decks4000 = new[] { "wander_4000_water.json", "wander_4000_stonewall.json" }, Decks8000 = new[] { "wander_8000_gravity.json", "wander_8000_labyrinth.json" }, HardShare = 0.4f },
            new() { Id = "rare_hunter", Label = "Rare Hunter", HeightCm = new(170f, 190f), Weight = new(0.3f, 0.6f), Hair = new[] { "marik_ishtar", "ryou_bakura", "storm_crown" }, HairHex = new[] { "#1B1B22", "#8A8C94" }, Shirt = new[] { "#43207A", "#2A1B4A" }, Shirt2 = new[] { "#E3B64C" }, Bottoms = new[] { "#0B0B10" }, Shoes = new[] { "#0B0B10" }, Decks4000 = new[] { "wander_4000_machine.json", "wander_4000_zombie.json" }, Decks8000 = new[] { "wander_8000_gravekeeper.json", "wander_8000_monarch.json" }, HardShare = 0.7f },
            new() { Id = "academy", Label = "Academy Student", HeightCm = new(158f, 180f), Weight = new(0.25f, 0.6f), Hair = new[] { "jaden_yuki", "alexis_rhodes", "yuma_tsukumo", "storm_crown" }, Shirt = new[] { "#C8302A", "#E8B84A", "#2F5FD0" }, Shirt2 = new[] { "#F2F2F5" }, Bottoms = new[] { "#F2F2F5", "#1C2340" }, Decks4000 = new[] { "wander_4000_play_union.json", "wander_4000_play_gemini.json", "wander_4000_play_ojama.json" }, Decks8000 = new[] { "wander_8000_toon.json", "wander_8000_monarch.json" }, HardShare = 0.35f },
            new() { Id = "biker", Label = "Rooftop Runner", HeightCm = new(170f, 188f), Weight = new(0.4f, 0.75f), Hair = new[] { "jack_atlas", "crimson_flame", "yusei_fudo" }, HairHex = new[] { "#E8B84A", "#1B1B22", "#3A2415" }, Shirt = new[] { "#111318", "#F2F2F5" }, Shirt2 = new[] { "#2F5FD0", "#C8302A" }, Bottoms = new[] { "#0B0B10", "#3A3A44" }, Decks4000 = new[] { "wander_4000_dino.json", "wander_4000_beatdown.json" }, Decks8000 = new[] { "wander_8000_harpie.json", "wander_8000_red_eyes.json" }, HardShare = 0.45f },
            new() { Id = "cafe", Label = "Café Regular", HeightCm = new(160f, 178f), Weight = new(0.3f, 0.65f), Hair = new[] { "tea_gardner", "storm_crown", "crimson_flame" }, Shirt = new[] { "#2F9E6B", "#F2A83B", "#8FB4E8" }, Shirt2 = new[] { "#F2F2F5" }, Bottoms = new[] { "#3A3A44", "#1C2340" }, Decks4000 = new[] { "wander_4000_play_burn.json", "wander_4000_water.json" }, Decks8000 = new[] { "wander_8000_toon.json", "wander_8000_gravity.json" }, HardShare = 0.3f },
            new() { Id = "magician", Label = "Street Magician", HeightCm = new(165f, 185f), Weight = new(0.25f, 0.55f), Hair = new[] { "yugi_muto", "marik_ishtar", "ryou_bakura" }, HairHex = new[] { "#1B1B22", "#43207A", "#DAD7EA" }, Shirt = new[] { "#43207A", "#0F5A6A" }, Shirt2 = new[] { "#E3B64C" }, Bottoms = new[] { "#0B0B10", "#1E0E3A" }, Decks4000 = new[] { "wander_4000_play_scorpion.json", "wander_4000_play_searchers.json" }, Decks8000 = new[] { "wander_8000_gravekeeper.json", "wander_8000_labyrinth.json" }, HardShare = 0.5f },
            new() { Id = "veteran", Label = "Alley Veteran", Sex = 0, HeightCm = new(172f, 190f), Weight = new(0.5f, 0.9f), Hair = new[] { "storm_crown", "jack_atlas" }, HairHex = new[] { "#55565E", "#8A8C94", "#1B1B22" }, Shirt = new[] { "#3A3A44", "#5A3A20" }, Shirt2 = new[] { "#2A2A33" }, Bottoms = new[] { "#2A2A33", "#0B0B10" }, Decks4000 = new[] { "wander_4000_stonewall.json", "wander_4000_jam.json" }, Decks8000 = new[] { "wander_8000_monarch.json", "wander_8000_labyrinth.json" }, HardShare = 0.6f },
            new() { Id = "park", Label = "Park Regular", HeightCm = new(150f, 180f), Weight = new(0.25f, 0.7f), Hair = new[] { "jaden_yuki", "tea_gardner", "yuma_tsukumo", "alexis_rhodes" }, Shirt = new[] { "#2F9E6B", "#F2F2F5", "#F2A83B" }, Shirt2 = new[] { "#1C2340" }, Bottoms = new[] { "#3A3A44", "#5A3A20" }, Decks4000 = new[] { "wander_4000_insect.json", "wander_4000_play_thunder.json" }, Decks8000 = new[] { "wander_8000_amazoness.json", "wander_8000_harpie.json" }, HardShare = 0.25f },
            new() { Id = "rift_touched", Label = "Rift-Touched Local", HeightCm = new(160f, 186f), Weight = new(0.2f, 0.5f), Hair = new[] { "marik_ishtar", "crimson_flame", "yugi_muto" }, HairHex = new[] { "#DAD7EA", "#3ECFFF", "#43207A" }, Shirt = new[] { "#0F5A6A", "#1E0E3A" }, Shirt2 = new[] { "#3ECFFF" }, Bottoms = new[] { "#0B0B10" }, Shoes = new[] { "#3ECFFF", "#111318" }, Decks4000 = new[] { "wander_4000_zombie.json", "wander_4000_machine.json" }, Decks8000 = new[] { "wander_8000_gravekeeper.json", "wander_8000_gravity.json" }, HardShare = 0.65f },
        };

        public static IReadOnlyList<Archetype> All => Archetypes;

        /// <summary>FNV-1a: stable across runs and platforms (string.GetHashCode is not).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var c in s ?? "") { h ^= c; h *= 16777619; }
                return (int)(h & 0x7FFFFFFF);
            }
        }

        static readonly Dictionary<string, NpcDuelist> Cache = new();

        /// <summary>The NPC behind a name: null when the name is not one of ours (a canon duelist, a lab name).</summary>
        public static bool TryGet(string name, out NpcDuelist npc)
        {
            npc = null;
            if (string.IsNullOrEmpty(name)) return false;
            if (Cache.TryGetValue(name, out npc)) return true;
            if (!LooksGenerated(name)) return false;
            npc = Build(name);
            Cache[name] = npc;
            return true;
        }

        /// <summary>Our names are "First Last" from the pools above.</summary>
        static bool LooksGenerated(string name)
        {
            var sp = name.IndexOf(' ');
            if (sp <= 0 || sp >= name.Length - 1) return false;
            var first = name.Substring(0, sp); var last = name.Substring(sp + 1);
            return (Array.IndexOf(FirstMale, first) >= 0 || Array.IndexOf(FirstFemale, first) >= 0) && Array.IndexOf(Last, last) >= 0;
        }

        static NpcDuelist Build(string name)
        {
            var seed = StableHash(name);
            var rng = new System.Random(seed);
            var first = name.Substring(0, name.IndexOf(' '));
            var sex = Array.IndexOf(FirstFemale, first) >= 0 ? 1 : 0;
            // an archetype that fits the name's sex
            var fits = new List<Archetype>();
            foreach (var a in Archetypes) if (a.Sex < 0 || a.Sex == sex) fits.Add(a);
            var arch = fits[rng.Next(fits.Count)];
            var band = rng.NextDouble() < arch.HardShare ? StreetLpBand.Street8000 : StreetLpBand.Street4000;
            return new NpcDuelist
            {
                Name = name, ArchetypeId = arch.Id, ArchetypeLabel = arch.Label, Sex = sex, Seed = seed, Band = band,
                Deck4000 = arch.Decks4000.Length > 0 ? arch.Decks4000[rng.Next(arch.Decks4000.Length)] : "wander_4000_beatdown.json",
                Deck8000 = arch.Decks8000.Length > 0 ? arch.Decks8000[rng.Next(arch.Decks8000.Length)] : "wander_8000_red_eyes.json",
            };
        }

        /// <summary>A random duelist for <paramref name="seed"/> (the name decides everything else).</summary>
        public static NpcDuelist Generate(int seed)
        {
            var rng = new System.Random(seed);
            var fem = rng.NextDouble() < 0.45;
            var name = (fem ? FirstFemale[rng.Next(FirstFemale.Length)] : FirstMale[rng.Next(FirstMale.Length)]) + " " + Last[rng.Next(Last.Length)];
            TryGet(name, out var npc);
            return npc;
        }

        /// <summary>Today's street duelists for the Vs AI list: the same set all day, distinct names.</summary>
        public static List<NpcDuelist> Today(int count)
        {
            var day = DateTime.UtcNow.Date;
            var seed = StableHash("street|" + day.ToString("yyyy-MM-dd"));
            var rng = new System.Random(seed);
            var list = new List<NpcDuelist>(); var seen = new HashSet<string>();
            for (var guard = 0; list.Count < count && guard < count * 10; guard++)
            {
                var npc = Generate(rng.Next());
                if (npc == null || !seen.Add(npc.Name)) continue;
                list.Add(npc);
            }
            return list;
        }

        /// <summary>The look for one of ours: archetype pools, then the name's own dice for the rest.</summary>
        public static AvatarAppearance LookFor(NpcDuelist npc)
        {
            var arch = Array.Find(Archetypes, x => x.Id == npc.ArchetypeId) ?? Archetypes[0];
            var rng = new System.Random(npc.Seed ^ 0x5F3759DF);
            var a = AvatarAppearance.Default();
            a.bodyType = npc.Sex;
            var fem = npc.Sex == 1;
            a.heightCm = Mathf.Round(Mathf.Clamp(Mathf.Lerp(arch.HeightCm.x, arch.HeightCm.y, (float)rng.NextDouble()),
                AvatarCatalog.HeightMinCm(fem), AvatarCatalog.HeightMaxCm(fem)));
            a.weight = Mathf.Lerp(arch.Weight.x, arch.Weight.y, (float)rng.NextDouble());
            var styles = AvatarCatalog.HairStyles;
            string hair = null;
            if (arch.Hair.Length > 0)
            {
                // only styles the catalogue has for this body
                var ok = new List<string>();
                foreach (var id in arch.Hair) foreach (var st in styles) if (st.id == id) { ok.Add(id); break; }
                if (ok.Count > 0) hair = ok[rng.Next(ok.Count)];
            }
            if (hair == null && styles.Count > 0) hair = styles[rng.Next(styles.Count)].id;
            if (hair != null) a.hairStyleId = hair;
            a.hairPattern = 1;                                    // solid recolour in the archetype's colour
            a.hairHex = arch.HairHex[rng.Next(arch.HairHex.Length)];
            a.SetPaint("Shirt.0", arch.Shirt[rng.Next(arch.Shirt.Length)]);
            a.SetPaint("Shirt.1", arch.Shirt2[rng.Next(arch.Shirt2.Length)]);
            a.SetPaint("Bottoms.0", arch.Bottoms[rng.Next(arch.Bottoms.Length)]);
            a.SetPaint("Shoes.0", arch.Shoes[rng.Next(arch.Shoes.Length)]);
            var skins = AvatarCatalog.SkinPresets;
            var sk = skins[rng.Next(skins.Length)];
            a.SetPaint("Body.0", sk);
            a.skinHex = sk;
            a.ClampToCatalog();
            return a;
        }
    }
}
