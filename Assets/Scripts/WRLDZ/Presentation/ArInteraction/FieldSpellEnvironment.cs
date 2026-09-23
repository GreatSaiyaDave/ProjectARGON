using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Atmosphere drifting through the street while a Field Spell is live.
    /// Each kind is a motion, not a texture — colour comes from the palette.
    /// </summary>
    public enum FieldMotes
    {
        None,
        Embers,   // rise + flicker (fire, infernal)
        Bubbles,  // slow rise with wobble (sea)
        Leaves,   // fall with sway (forest, earth)
        Dust,     // low drift along the street (wasteland, valley)
        Pollen,   // hover near head height (meadow, sanctuary)
        Wind,     // fast streaks across the street (sky, mountain)
        Sparks,   // short jittery flashes (plasma, light, energy)
        Wisps,    // slow rise + swirl (darkness, vortex)
        Feathers  // slow fall, wide sway (sky temple, harpies)
    }

    /// <summary>
    /// One Field Spell's Solid Vision look. These rows are the field spell
    /// "assets": no prefabs or authored scenes. <see cref="ArFieldSpellFloor"/>
    /// builds every layer from the palette plus the card's own illustration.
    /// See <c>Docs/FIELD_SPELL_SOLID_VISION.md</c>.
    /// </summary>
    public readonly struct FieldSpellEnvironment
    {
        public readonly int CardId;
        public readonly string Key;
        /// <summary>Upper register — wall tint and the sweep ring.</summary>
        public readonly Color Sky;
        /// <summary>Terrain pad under each monster.</summary>
        public readonly Color Ground;
        /// <summary>Motes, boon aura, sweep crest.</summary>
        public readonly Color Accent;
        public readonly FieldMotes Motes;
        /// <summary>0…1 share of the mote pool that is alive.</summary>
        public readonly float MoteDensity;
        /// <summary>0…<see cref="FieldSpellEnvironments.MaxWash"/> tint on arena holos.</summary>
        public readonly float Wash;
        /// <summary>True for hand-tuned rows; false when derived from art.</summary>
        public readonly bool Curated;

        public FieldSpellEnvironment(int cardId, string key, Color sky, Color ground, Color accent,
            FieldMotes motes, float moteDensity, float wash, bool curated)
        {
            CardId = cardId;
            Key = key;
            Sky = sky;
            Ground = ground;
            Accent = accent;
            Motes = motes;
            MoteDensity = Mathf.Clamp01(moteDensity);
            Wash = Mathf.Clamp(wash, 0f, FieldSpellEnvironments.MaxWash);
            Curated = curated;
        }

        /// <summary>Wash colour for arena holos: white pulled toward the sky/accent mix.</summary>
        public Color WashTint => Color.Lerp(Sky, Accent, 0.5f);
    }

    /// <summary>
    /// Curated table for every Field Spell in the pool, plus an art-derived
    /// fallback so later sets get an environment with no new assets.
    /// Families (see the doc): Duelist Kingdom terrains buff by Type;
    /// attribute zones are +500/−400 by Attribute; the rest are named fields.
    /// </summary>
    public static class FieldSpellEnvironments
    {
        /// <summary>Hard cap on the holo tint so card art always stays legible.</summary>
        public const float MaxWash = 0.2f;

        /// <summary>
        /// Illustration window on a full card scan, bottom-left origin, with
        /// a small inset past the bevel. Measured on the 268×391 LOB/MRD scans
        /// (art box x 0.112–0.892, y 0.294–0.824). Tighter than
        /// <see cref="CardArtFocus.ArtworkNormRect"/>, which still catches the
        /// teal "SPELL CARD" strip and side frame.
        /// </summary>
        public static readonly Rect IllustrationInset = new(0.125f, 0.305f, 0.755f, 0.505f);

        /// <summary>KaibaCorp cyan when there is neither a row nor readable art.</summary>
        public static readonly FieldSpellEnvironment Neutral = new(
            0, "Neutral", H("#3a6f8a"), H("#26414f"), H("#33e5ff"),
            FieldMotes.Pollen, 0.25f, 0f, curated: false);

        static readonly Dictionary<int, FieldSpellEnvironment> Curated = Build();
        static readonly Dictionary<int, FieldSpellEnvironment> Derived = new();

        public static IReadOnlyDictionary<int, FieldSpellEnvironment> CuratedRows => Curated;

        static Dictionary<int, FieldSpellEnvironment> Build()
        {
            var d = new Dictionary<int, FieldSpellEnvironment>();

            void E(int id, string key, string sky, string ground, string accent,
                FieldMotes motes, float density, float wash) =>
                d[id] = new FieldSpellEnvironment(id, key, H(sky), H(ground), H(accent),
                    motes, density, wash, curated: true);

            // ── Duelist Kingdom terrains (+200 ATK/DEF by Type) ──────────────
            E(59197169, "Yami", "#1a0710", "#3a1426", "#d84577", FieldMotes.Wisps, 0.55f, 0.18f);
            E(87430998, "Forest", "#2f6a2c", "#5f8f38", "#8ee05c", FieldMotes.Leaves, 0.45f, 0.08f);
            E(50913601, "Mountain", "#7fa6c4", "#5e5860", "#6cb6ec", FieldMotes.Wind, 0.35f, 0.06f);
            E(86318356, "Sogen", "#4a9aa6", "#6a9e3c", "#9ee070", FieldMotes.Pollen, 0.35f, 0.06f);
            E(22702055, "Umi", "#3aa0d4", "#1a5ea8", "#2fb4f0", FieldMotes.Bubbles, 0.55f, 0.14f);
            E(23424603, "Wasteland", "#8a7c6a", "#5a4332", "#d89a5e", FieldMotes.Dust, 0.45f, 0.10f);

            // ── Attribute zones (+500 ATK / −400 DEF by Attribute) ───────────
            E(19384334, "Molten Destruction", "#2b1510", "#5c240c", "#ff7a22", FieldMotes.Embers, 0.70f, 0.16f);
            E(18161786, "Mystic Plasma Zone", "#3c2f78", "#2a2d48", "#8b6cff", FieldMotes.Sparks, 0.50f, 0.16f);
            // Art is mostly white rays; the vivid pixels are the red fiend. LIGHT reads white-gold.
            E(81777047, "Luminous Spark", "#f2ecd8", "#b8aa8a", "#ffe27a", FieldMotes.Sparks, 0.45f, 0.08f);
            E(56594520, "Gaia Power", "#24391a", "#8a7f56", "#9bd86a", FieldMotes.Leaves, 0.40f, 0.08f);
            E(45778932, "Rising Air Current", "#4c78aa", "#6a98cc", "#9fd4ff", FieldMotes.Wind, 0.60f, 0.08f);
            E(82999629, "Umiiruka", "#5588d8", "#5a9cc6", "#4cb8f5", FieldMotes.Bubbles, 0.50f, 0.12f);

            // ── Named / archetype fields ─────────────────────────────────────
            E(295517, "A Legendary Ocean", "#7a9cb6", "#506a76", "#7cc8ec", FieldMotes.Bubbles, 0.50f, 0.12f);
            E(75782277, "Harpies' Hunting Ground", "#8a6d48", "#8a8a62", "#e2bd6a", FieldMotes.Feathers, 0.35f, 0.06f);
            E(47355498, "Necrovalley", "#7a5238", "#6e5444", "#f08a2c", FieldMotes.Dust, 0.40f, 0.12f);
            E(94585852, "Pandemonium", "#8a3a12", "#5c5c1a", "#ff5a14", FieldMotes.Embers, 0.55f, 0.16f);
            // Art-derived accent is deep blue; the anime temple reads gold on cloud white.
            E(56433456, "The Sanctuary in the Sky", "#6f8fc2", "#d8d6cf", "#ffe08a", FieldMotes.Feathers, 0.40f, 0.06f);
            E(81380218, "Chorus of Sanctuary", "#6a94b4", "#c89a96", "#ff9fb8", FieldMotes.Pollen, 0.35f, 0.06f);
            E(69296555, "Array of Revealing Light", "#2c2860", "#4f47a0", "#7a82ff", FieldMotes.Sparks, 0.35f, 0.10f);
            E(1801154, "Centrifugal Field", "#5a2e3a", "#2e6a4a", "#f07a3c", FieldMotes.Wisps, 0.40f, 0.10f);
            E(33550694, "Fusion Gate", "#413850", "#3c6e4a", "#8a6cf0", FieldMotes.Sparks, 0.40f, 0.12f);
            return d;
        }

        /// <summary>
        /// Environment for a live Field Spell: curated row first, then one
        /// derived from its illustration (cached), then <see cref="Neutral"/>.
        /// </summary>
        public static FieldSpellEnvironment Resolve(CardDef def, CardDatabase db)
        {
            if (def == null) return Neutral;
            if (Curated.TryGetValue(def.id, out var row)) return row;
            if (Derived.TryGetValue(def.id, out var cached)) return cached;

            var art = db?.GetArt(def.id);
            var env = FromArtwork(def.id, def.name, art != null ? art.texture : null);
            Derived[def.id] = env;
            return env;
        }

        /// <summary>
        /// Derive a palette from a card illustration: sky = top third mean,
        /// ground = bottom third mean, accent = heaviest saturation-weighted hue
        /// bin (plain averaging turns two-hue art into mud). Mote kind follows
        /// the accent hue.
        /// </summary>
        public static FieldSpellEnvironment FromArtwork(int cardId, string key, Texture2D tex)
        {
            if (tex == null || !tex.isReadable) return Neutral;

            var r = CardArtFocus.LooksLikeFullCardScan(tex) && !CardArtFocus.IsPreCroppedIllustration(tex)
                ? IllustrationInset
                : new Rect(0f, 0f, 1f, 1f);

            const int grid = 48;
            const int bins = 12;
            var binSum = new Vector4[bins];
            Vector3 skySum = default, groundSum = default;
            int skyN = 0, groundN = 0;

            for (var gy = 0; gy < grid; gy++)
            {
                // gy = 0 is the bottom row (Unity texture origin)
                var v = r.y + r.height * (gy + 0.5f) / grid;
                for (var gx = 0; gx < grid; gx++)
                {
                    var u = r.x + r.width * (gx + 0.5f) / grid;
                    var c = tex.GetPixelBilinear(u, v);
                    if (gy >= grid * 2 / 3) { skySum += new Vector3(c.r, c.g, c.b); skyN++; }
                    else if (gy < grid / 3) { groundSum += new Vector3(c.r, c.g, c.b); groundN++; }

                    Color.RGBToHSV(c, out var h, out var s, out var val);
                    var w = s * s * val;
                    if (w < 0.02f) continue;
                    var k = Mathf.Clamp((int)(h * bins), 0, bins - 1);
                    binSum[k] += new Vector4(c.r * w, c.g * w, c.b * w, w);
                }
            }

            var best = binSum[0];
            for (var i = 1; i < bins; i++)
                if (binSum[i].w > best.w) best = binSum[i];

            var sky = skyN > 0 ? ToColor(skySum / skyN) : Neutral.Sky;
            var ground = groundN > 0 ? ToColor(groundSum / groundN) : Neutral.Ground;
            var accent = Neutral.Accent;
            if (best.w > 0f)
            {
                var mean = new Color(best.x / best.w, best.y / best.w, best.z / best.w, 1f);
                Color.RGBToHSV(mean, out var h, out var s, out var val);
                // Lift to hologram brightness so it reads on passthrough.
                accent = Color.HSVToRGB(h, Mathf.Max(s, 0.55f), Mathf.Max(val, 0.85f));
            }

            return new FieldSpellEnvironment(cardId, key ?? "Derived", sky, ground, accent,
                MotesForHue(accent), 0.40f, 0.08f, curated: false);
        }

        /// <summary>Accent hue → mote motion for derived rows.</summary>
        public static FieldMotes MotesForHue(Color accent)
        {
            Color.RGBToHSV(accent, out var h, out var s, out _);
            if (s < 0.2f) return FieldMotes.Pollen;
            var deg = h * 360f;
            if (deg < 45f || deg >= 330f) return FieldMotes.Embers;   // red / orange
            if (deg < 75f) return FieldMotes.Pollen;                  // gold
            if (deg < 165f) return FieldMotes.Leaves;                 // green
            if (deg < 235f) return FieldMotes.Bubbles;                // cyan / blue
            return FieldMotes.Sparks;                                 // violet / magenta
        }

        static Color ToColor(Vector3 v) => new(v.x, v.y, v.z, 1f);

        static Color H(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }
}
