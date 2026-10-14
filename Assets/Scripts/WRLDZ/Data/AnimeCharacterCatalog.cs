using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// 2D anime character catalog: maps a character id to its sprite set (one texture per camera angle),
    /// layer splits for animation, and expression variants. Sprites are cel-shaded anime art displayed
    /// on billboard planes in 3D space (AnimeCelShader), NOT inked — the player experiences the anime.
    /// </summary>
    [Serializable]
    public class AnimeCharacterSet
    {
        public string id;
        public string displayName;

        /// <summary>Camera-angle sprites: front, three-quarter, side, back. At least front must exist.</summary>
        [Serializable] public class AngleSprites
        {
            public string front = "";
            public string threeQuarter = "";
            public string side = "";
            public string back = "";
        }
        public AngleSprites angles = new();

        /// <summary>Layer splits for animation (body, hair, clothes as separate textures with alpha).</summary>
        [Serializable] public class LayerSet
        {
            public string body = "";
            public string hair = "";
            public string clothes = "";
        }
        public LayerSet layers = new();

        /// <summary>Expression variants (neutral, serious, smiling) — face region swaps.</summary>
        [Serializable] public class Expressions
        {
            public string neutral = "";
            public string serious = "";
            public string smiling = "";
        }
        public Expressions expressions = new();

        /// <summary>Gesture overlays (draw, summon, attack) — pre-drawn arm/hand poses.</summary>
        [Serializable] public class Gestures
        {
            public string idle = "";
            public string draw = "";
            public string summon = "";
            public string attack = "";
            public string defeat = "";
        }
        public Gestures gestures = new();

        /// <summary>Life-size height in metres (Yami Yugi 1.58, Kaiba 1.86, etc.).</summary>
        public float heightM = 1.75f;

        /// <summary>Shadow blob diameter in metres.</summary>
        public float shadowDiameter = 0.9f;
    }

    /// <summary>
    /// The full DM cast as 2D anime character sets. Sprites live under
    /// Resources/WRLDZ/AnimeCharacters/{id}/ as PNG textures. This catalog maps ids to the
    /// sprite paths; the AnimeCharacterActor loads and displays them.
    /// </summary>
    public static class AnimeCharacterCatalog
    {
        /// <summary>Resource stem for a character's sprite folder.</summary>
        public static string ResPath(string id) => $"WRLDZ/AnimeCharacters/{id}";

        /// <summary>All registered character sets. Add new characters here.</summary>
        public static readonly List<AnimeCharacterSet> All = new()
        {
            new AnimeCharacterSet
            {
                id = "yami_yugi",
                displayName = "Yami Yugi",
                angles = new AnimeCharacterSet.AngleSprites { front = "yami_yugi_front", threeQuarter = "yami_yugi_34", side = "yami_yugi_side", back = "yami_yugi_back" },
                expressions = new AnimeCharacterSet.Expressions { neutral = "neutral", serious = "serious", smiling = "smiling" },
                gestures = new AnimeCharacterSet.Gestures { idle = "idle", draw = "draw", summon = "summon", attack = "attack", defeat = "defeat" },
                heightM = 1.58f,
                shadowDiameter = 0.6f,
            },
            new AnimeCharacterSet
            {
                id = "kaiba",
                displayName = "Seto Kaiba",
                angles = new AnimeCharacterSet.AngleSprites { front = "kaiba_front", threeQuarter = "kaiba_34", side = "kaiba_side", back = "kaiba_back" },
                expressions = new AnimeCharacterSet.Expressions { neutral = "neutral", serious = "serious", smiling = "smiling" },
                gestures = new AnimeCharacterSet.Gestures { idle = "idle", draw = "draw", summon = "summon", attack = "attack", defeat = "defeat" },
                heightM = 1.86f,
                shadowDiameter = 0.7f,
            },
            new AnimeCharacterSet
            {
                id = "joey",
                displayName = "Joey Wheeler",
                angles = new AnimeCharacterSet.AngleSprites { front = "joey_front", threeQuarter = "joey_34", side = "joey_side", back = "joey_back" },
                expressions = new AnimeCharacterSet.Expressions { neutral = "neutral", serious = "serious", smiling = "smiling" },
                gestures = new AnimeCharacterSet.Gestures { idle = "idle", draw = "draw", summon = "summon", attack = "attack", defeat = "defeat" },
                heightM = 1.78f,
                shadowDiameter = 0.65f,
            },
            new AnimeCharacterSet
            {
                id = "mai",
                displayName = "Mai Valentine",
                angles = new AnimeCharacterSet.AngleSprites { front = "mai_front", threeQuarter = "mai_34", side = "mai_side", back = "mai_back" },
                heightM = 1.70f,
            },
            new AnimeCharacterSet
            {
                id = "marik",
                displayName = "Marik Ishtar",
                angles = new AnimeCharacterSet.AngleSprites { front = "marik_front", threeQuarter = "marik_34", side = "marik_side", back = "marik_back" },
                heightM = 1.77f,
            },
            new AnimeCharacterSet
            {
                id = "bakura",
                displayName = "Yami Bakura",
                angles = new AnimeCharacterSet.AngleSprites { front = "bakura_front", threeQuarter = "bakura_34", side = "bakura_side", back = "bakura_back" },
                heightM = 1.76f,
            },
            new AnimeCharacterSet
            {
                id = "pegasus",
                displayName = "Maximillion Pegasus",
                angles = new AnimeCharacterSet.AngleSprites { front = "pegasus_front", threeQuarter = "pegasus_34", side = "pegasus_side", back = "pegasus_back" },
                heightM = 1.90f,
            },
            new AnimeCharacterSet
            {
                id = "tea",
                displayName = "Téa Gardner",
                angles = new AnimeCharacterSet.AngleSprites { front = "tea_front", threeQuarter = "tea_34", side = "tea_side", back = "tea_back" },
                heightM = 1.64f,
            },
            new AnimeCharacterSet
            {
                id = "weevil",
                displayName = "Weevil Underwood",
                angles = new AnimeCharacterSet.AngleSprites { front = "weevil_front", threeQuarter = "weevil_34", side = "weevil_side", back = "weevil_back" },
                heightM = 1.52f,
            },
            new AnimeCharacterSet
            {
                id = "rex",
                displayName = "Rex Raptor",
                angles = new AnimeCharacterSet.AngleSprites { front = "rex_front", threeQuarter = "rex_34", side = "rex_side", back = "rex_back" },
                heightM = 1.70f,
            },
            new AnimeCharacterSet
            {
                id = "mako",
                displayName = "Mako Tsunami",
                angles = new AnimeCharacterSet.AngleSprites { front = "mako_front", threeQuarter = "mako_34", side = "mako_side", back = "mako_back" },
                heightM = 1.83f,
            },
            new AnimeCharacterSet
            {
                id = "tristan",
                displayName = "Tristan Taylor",
                angles = new AnimeCharacterSet.AngleSprites { front = "tristan_front", threeQuarter = "tristan_34", side = "tristan_side", back = "tristan_back" },
                heightM = 1.80f,
            },
            new AnimeCharacterSet
            {
                id = "mokuba",
                displayName = "Mokuba Kaiba",
                angles = new AnimeCharacterSet.AngleSprites { front = "mokuba_front", threeQuarter = "mokuba_34", side = "mokuba_side", back = "mokuba_back" },
                heightM = 1.55f,
            },
            new AnimeCharacterSet
            {
                id = "ishizu",
                displayName = "Ishizu Ishtar",
                angles = new AnimeCharacterSet.AngleSprites { front = "ishizu_front", threeQuarter = "ishizu_34", side = "ishizu_side", back = "ishizu_back" },
                heightM = 1.72f,
            },
            new AnimeCharacterSet
            {
                id = "odion",
                displayName = "Odion",
                angles = new AnimeCharacterSet.AngleSprites { front = "odion_front", threeQuarter = "odion_34", side = "odion_side", back = "odion_back" },
                heightM = 1.88f,
            },
        };

        public static AnimeCharacterSet Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < All.Count; i++)
                if (All[i].id == id) return All[i];
            return null;
        }

        /// <summary>The camera angle category for a character sprite.</summary>
        public enum CameraAngle { Front, ThreeQuarter, Side, Back }

        /// <summary>Pick the closest pre-drawn angle for a camera azimuth (radians, 0 = front).</summary>
        public static CameraAngle AngleFor(float azimuthRad)
        {
            var a = Mathf.Abs(azimuthRad);
            if (a < Mathf.PI * 0.25f) return CameraAngle.Front;
            if (a < Mathf.PI * 0.75f) return CameraAngle.ThreeQuarter;
            if (a < Mathf.PI * 1.25f) return CameraAngle.Side;
            return CameraAngle.Back;
        }

        /// <summary>Texture name for a given angle.</summary>
        public static string AngleSprite(AnimeCharacterSet set, CameraAngle angle)
        {
            if (set == null) return "";
            switch (angle)
            {
                case CameraAngle.Front: return set.angles.front;
                case CameraAngle.ThreeQuarter: return set.angles.threeQuarter;
                case CameraAngle.Side: return set.angles.side;
                case CameraAngle.Back: return set.angles.back;
            }
            return "";
        }
    }
}
