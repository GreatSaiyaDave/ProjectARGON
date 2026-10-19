using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// The animated face's catalogue (2026-10-18; owner: "several eyes, noses, mouths, head types, ears, skin tone … all
    /// the eyes and mouths fully animated"). Every entry is a set of parameters for the WRLDZ/AvatarAnimeFace shader
    /// (signed-distance anime features drawn on the face), so a choice is a shape, not a sprite: it blinks, glances,
    /// smiles and speaks through the same parameters. Ids persist on the account (AvatarAppearance.eyeId …).
    /// Shapes are style choices in the Duel Monsters anime idiom (angular, round, narrow, drooping, tilted, hooded) and
    /// add no lore; iris colours are the palette the series draws its duelists with.
    /// </summary>
    public static class FaceCatalog
    {
        public sealed class EyeShape
        {
            public string Id, Name, Blurb;
            public float W = 0.40f, H = 0.26f, Tilt = 0.06f, Round = 0.5f, TopBulge = 1f, BotBulge = 0.55f;
            public float Lash = 0.055f, LashOuter = 1.6f, LowerLash = 0.012f, Iris = 0.23f, IrisScaleY = 1.15f, Pupil = 0.085f;
            /// <summary>Resting lid (0 wide open … 0.35 heavy-lidded).</summary>
            public float RestLid;
        }

        public sealed class MouthShape
        {
            public string Id, Name, Blurb;
            public float W = 0.30f, LipLine = 0.035f, LowerLip = 0.3f, RestCorner = 0.1f, OpenShape = 0.3f;
        }

        public sealed class NoseShape
        {
            public string Id, Name, Blurb;
            public int Style;
            public float Size = 0.5f, Shade = 0.35f;
        }

        public sealed class BrowShape
        {
            public string Id, Name, Blurb;
            public float Thick = 0.09f, Arch = 0.12f, Tilt = 0f;
        }

        public static readonly EyeShape[] Eyes =
        {
            new() { Id = "eye_sharp", Name = "Sharp", Blurb = "Angular and narrow, the outer corner high.", W = 0.42f, H = 0.21f, Tilt = 0.10f, Round = 0.2f, TopBulge = 0.95f, BotBulge = 0.45f, Lash = 0.06f, LashOuter = 1.9f, Iris = 0.20f, IrisScaleY = 1.2f, RestLid = 0.08f },
            new() { Id = "eye_round", Name = "Round", Blurb = "Big and open, the iris large.", W = 0.38f, H = 0.30f, Tilt = 0.02f, Round = 0.85f, TopBulge = 1.05f, BotBulge = 0.75f, Lash = 0.05f, LashOuter = 1.3f, Iris = 0.27f, IrisScaleY = 1.1f, Pupil = 0.10f },
            new() { Id = "eye_narrow", Name = "Narrow", Blurb = "Long and calm, half-lidded.", W = 0.44f, H = 0.19f, Tilt = 0.04f, Round = 0.3f, TopBulge = 0.9f, BotBulge = 0.4f, Lash = 0.05f, LashOuter = 1.5f, Iris = 0.19f, RestLid = 0.18f },
            new() { Id = "eye_soft", Name = "Soft", Blurb = "Gentle, the outer corner low.", W = 0.40f, H = 0.26f, Tilt = -0.04f, Round = 0.6f, TopBulge = 1.0f, BotBulge = 0.6f, Lash = 0.05f, LashOuter = 1.4f, Iris = 0.24f },
            new() { Id = "eye_tilted", Name = "Tilted", Blurb = "Cat-like, long lashes, the corner swept up.", W = 0.42f, H = 0.25f, Tilt = 0.14f, Round = 0.45f, TopBulge = 1.0f, BotBulge = 0.5f, Lash = 0.07f, LashOuter = 2.2f, LowerLash = 0.016f, Iris = 0.23f },
            new() { Id = "eye_hooded", Name = "Hooded", Blurb = "Heavy upper lid, a hard stare.", W = 0.41f, H = 0.24f, Tilt = 0.06f, Round = 0.35f, TopBulge = 0.85f, BotBulge = 0.5f, Lash = 0.065f, LashOuter = 1.7f, Iris = 0.22f, RestLid = 0.28f },
            new() { Id = "eye_wide", Name = "Wide", Blurb = "Tall and startled-bright.", W = 0.39f, H = 0.32f, Tilt = 0.05f, Round = 0.7f, TopBulge = 1.15f, BotBulge = 0.7f, Lash = 0.05f, LashOuter = 1.4f, Iris = 0.25f, IrisScaleY = 1.2f, Pupil = 0.09f },
        };

        /// <summary>Iris colours: the hues the series paints its duelists' eyes in, plus the fine tune in the customizer.</summary>
        public static readonly (string name, string hex)[] IrisColours =
        {
            ("Violet", "#5B3A92"), ("Blue", "#2E5FBF"), ("Brown", "#6B4323"), ("Green", "#2F8A5B"), ("Amber", "#C98A2E"),
            ("Grey", "#6E7480"), ("Red", "#B8362F"), ("Teal", "#1F8F95"), ("Pink", "#C7508F"), ("Black", "#1B1A22"),
        };

        public static readonly MouthShape[] Mouths =
        {
            new() { Id = "mouth_thin", Name = "Thin", Blurb = "A thin, even line.", W = 0.30f, LipLine = 0.028f, LowerLip = 0.15f, RestCorner = 0.05f },
            new() { Id = "mouth_full", Name = "Full", Blurb = "Fuller lips, a soft lower lip.", W = 0.30f, LipLine = 0.042f, LowerLip = 0.55f, RestCorner = 0.08f },
            new() { Id = "mouth_wide", Name = "Wide", Blurb = "Wide and expressive.", W = 0.38f, LipLine = 0.034f, LowerLip = 0.3f, RestCorner = 0.12f, OpenShape = 0.15f },
            new() { Id = "mouth_small", Name = "Small", Blurb = "Small and neat.", W = 0.22f, LipLine = 0.032f, LowerLip = 0.3f, RestCorner = 0.1f, OpenShape = 0.6f },
            new() { Id = "mouth_set", Name = "Set", Blurb = "Straight and firm, corners down a touch.", W = 0.33f, LipLine = 0.036f, LowerLip = 0.2f, RestCorner = -0.08f },
        };

        public static readonly NoseShape[] Noses =
        {
            new() { Id = "nose_tick", Name = "Tick", Blurb = "The anime tick under the tip.", Style = 0, Size = 0.5f, Shade = 0.35f },
            new() { Id = "nose_line", Name = "Line", Blurb = "A bridge shadow down one side.", Style = 1, Size = 0.55f, Shade = 0.32f },
            new() { Id = "nose_point", Name = "Point", Blurb = "A sharp tip's small shadow.", Style = 2, Size = 0.55f, Shade = 0.4f },
            new() { Id = "nose_button", Name = "Button", Blurb = "Round and soft.", Style = 3, Size = 0.5f, Shade = 0.3f },
            new() { Id = "nose_none", Name = "Bare", Blurb = "No drawn nose; the face shape alone.", Style = 0, Size = 0.5f, Shade = 0f },
        };

        public static readonly BrowShape[] Brows =
        {
            new() { Id = "brow_straight", Name = "Straight", Blurb = "Level and even.", Thick = 0.085f, Arch = 0.03f },
            new() { Id = "brow_arched", Name = "Arched", Blurb = "A clean high arch.", Thick = 0.075f, Arch = 0.16f },
            new() { Id = "brow_thick", Name = "Thick", Blurb = "Heavy and bold.", Thick = 0.13f, Arch = 0.08f },
            new() { Id = "brow_thin", Name = "Thin", Blurb = "Fine and light.", Thick = 0.05f, Arch = 0.11f },
            new() { Id = "brow_fierce", Name = "Fierce", Blurb = "Angled in at rest.", Thick = 0.10f, Arch = 0.05f, Tilt = 0.35f },
        };

        public const string DefaultEye = "eye_sharp", DefaultMouth = "mouth_thin", DefaultNose = "nose_tick", DefaultBrow = "brow_straight";
        public const string DefaultIris = "#5B3A92";

        public static EyeShape Eye(string id) { foreach (var e in Eyes) if (e.Id == id) return e; return Eyes[0]; }
        public static MouthShape Mouth(string id) { foreach (var e in Mouths) if (e.Id == id) return e; return Mouths[0]; }
        public static NoseShape Nose(string id) { foreach (var e in Noses) if (e.Id == id) return e; return Noses[0]; }
        public static BrowShape Brow(string id) { foreach (var e in Brows) if (e.Id == id) return e; return Brows[0]; }

        public static bool HasEye(string id) { foreach (var e in Eyes) if (e.Id == id) return true; return false; }
        public static bool HasMouth(string id) { foreach (var e in Mouths) if (e.Id == id) return true; return false; }
        public static bool HasNose(string id) { foreach (var e in Noses) if (e.Id == id) return true; return false; }
        public static bool HasBrow(string id) { foreach (var e in Brows) if (e.Id == id) return true; return false; }

        /// <summary>
        /// Named expressions: lid, lid curve (happy ^), lower lid (squint), brow lift / tilt, mouth corners / open, iris
        /// size (fear shrinks it). Blended over time by AvatarFaceRig; the duel beats pick one.
        /// </summary>
        public struct Expression
        {
            public float Lid, LidCurve, LowerLid, BrowLift, BrowTilt, Corner, Open, OpenShape, Teeth, IrisScale;
            public static Expression Neutral => new() { IrisScale = 1f };
        }

        public static Expression ExpressionFor(string mood) => mood switch
        {
            "determined" => new Expression { Lid = 0.12f, LowerLid = 0.25f, BrowLift = -0.03f, BrowTilt = 0.45f, Corner = -0.15f, IrisScale = 0.95f },
            "angry" => new Expression { Lid = 0.18f, LowerLid = 0.35f, BrowLift = -0.06f, BrowTilt = 0.8f, Corner = -0.5f, Open = 0.25f, OpenShape = 0.1f, Teeth = 1f, IrisScale = 0.85f },
            "happy" => new Expression { Lid = 0.1f, LowerLid = 0.3f, BrowLift = 0.03f, Corner = 0.9f, Open = 0.2f, OpenShape = 0.2f, Teeth = 0.8f, IrisScale = 1.05f },
            "laugh" => new Expression { Lid = 1f, LidCurve = 1f, BrowLift = 0.05f, Corner = 1f, Open = 0.65f, OpenShape = 0.2f, Teeth = 0.9f, IrisScale = 1f },
            "smug" => new Expression { Lid = 0.3f, LowerLid = 0.15f, BrowLift = 0.02f, BrowTilt = -0.15f, Corner = 0.6f, IrisScale = 1f },
            "surprised" => new Expression { Lid = -0.15f, BrowLift = 0.09f, BrowTilt = -0.2f, Corner = -0.1f, Open = 0.55f, OpenShape = 0.9f, Teeth = 0.2f, IrisScale = 0.7f },
            "pain" => new Expression { Lid = 0.75f, LidCurve = -0.2f, LowerLid = 0.6f, BrowLift = 0.02f, BrowTilt = 0.6f, Corner = -0.6f, Open = 0.4f, OpenShape = 0.05f, Teeth = 1f, IrisScale = 0.8f },
            "sad" => new Expression { Lid = 0.35f, LowerLid = 0.1f, BrowLift = 0.04f, BrowTilt = -0.7f, Corner = -0.55f, IrisScale = 1.05f },
            "scheme" => new Expression { Lid = 0.4f, LowerLid = 0.2f, BrowLift = -0.02f, BrowTilt = 0.3f, Corner = 0.45f, IrisScale = 0.9f },
            _ => Expression.Neutral
        };

        public static Expression Lerp(Expression a, Expression b, float t) => new()
        {
            Lid = Mathf.Lerp(a.Lid, b.Lid, t), LidCurve = Mathf.Lerp(a.LidCurve, b.LidCurve, t), LowerLid = Mathf.Lerp(a.LowerLid, b.LowerLid, t),
            BrowLift = Mathf.Lerp(a.BrowLift, b.BrowLift, t), BrowTilt = Mathf.Lerp(a.BrowTilt, b.BrowTilt, t), Corner = Mathf.Lerp(a.Corner, b.Corner, t),
            Open = Mathf.Lerp(a.Open, b.Open, t), OpenShape = Mathf.Lerp(a.OpenShape, b.OpenShape, t), Teeth = Mathf.Lerp(a.Teeth, b.Teeth, t),
            IrisScale = Mathf.Lerp(a.IrisScale, b.IrisScale, t)
        };

        /// <summary>A duel beat (ArOpponentDuelist.Play) → the face it wears, and for how long before it relaxes (s; 0 = hold).</summary>
        public static (string mood, float hold) MoodForBeat(string beat) => beat switch
        {
            "start" => ("determined", 2.5f), "turn" => ("determined", 1.5f), "attack" => ("angry", 2.2f), "good" => ("smug", 2.5f),
            "cast" => ("determined", 1.8f), "cast_big" => ("angry", 2.5f), "taunt" => ("smug", 3f), "scheme" => ("scheme", 3f),
            "hit" => ("pain", 1.6f), "hit_big" => ("pain", 3.5f), "reveal" => ("surprised", 1.8f), "wait" => ("neutral", 0f),
            "victory" => ("laugh", 0f), "defeat" => ("sad", 0f),
            _ => ("neutral", 0f)
        };
    }
}
