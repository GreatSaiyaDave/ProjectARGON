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

        /// <summary>
        /// A canon duelist's living face: the same shape parameters, measured from the sourced art (the Faces/&lt;id&gt;.png
        /// band built from official Duel Links art / series frames, checked against the owner's episode library), so the
        /// animated face carries the character instead of the flat decal. Keyed by <c>CharacterLook.FaceId</c>.
        /// <see cref="HideEye"/> hides one animated eye where the art shows something else there (Pegasus's Millennium
        /// Eye, drawn from the band's own half); <see cref="KeepDecal"/> leaves the painted band over the animated face
        /// (opaque sunglasses). Entries come from Tools/avatar/canon_faces.json (the measurement record).
        /// </summary>
        public sealed class CharacterFace
        {
            public string FaceId, Name, Source;
            public EyeShape Eye;
            public string IrisHex;
            public BrowShape Brow;
            public float BrowPeak = 0.25f;
            public string BrowHex;
            public NoseShape Nose;
            public MouthShape Mouth;
            /// <summary>0 none, 1 the character's own left eye, 2 the right.</summary>
            public int HideEye;
            public bool KeepDecal;
        }

        public static readonly List<CharacterFace> Characters = new()
        {
            // CANON_FACES_BEGIN (generated from Tools/avatar/canon_faces.json by Tools/avatar/canon_faces_to_cs.py)
            new() { FaceId = "tea", Name = "Téa Gardner", Source = "Faces/tea.png + series frames; unverified; conf 0.72",
                Eye = new EyeShape { Id = "canon_tea", W = 0.39f, H = 0.34f, Tilt = -0.02f, Round = 0.9f, TopBulge = 0.85f, BotBulge = 0.9f, Lash = 0.06f, LashOuter = 2f, LowerLash = 0.017f, Iris = 0.21f, IrisScaleY = 1.3f, Pupil = 0.065f, RestLid = 0.03f },
                IrisHex = "#50799C",
                Brow = new BrowShape { Id = "canon_tea", Thick = 0.045f, Arch = 0.13f, Tilt = 0f }, BrowPeak = 0.3f, BrowHex = "#24231E",
                Nose = new NoseShape { Id = "canon_tea", Style = 2, Size = 0.6f, Shade = 0.4f },
                Mouth = new MouthShape { Id = "canon_tea", W = 0.18f, LipLine = 0.03f, LowerLip = 0.15f, RestCorner = -0.1f, OpenShape = 0.65f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "yugi_muto", Name = "Yugi Muto (little Yugi)", Source = "Faces/yugi_muto.png + series frames; unverified; conf 0.7",
                Eye = new EyeShape { Id = "canon_yugi_muto", W = 0.38f, H = 0.34f, Tilt = 0.13f, Round = 0.8f, TopBulge = 1.2f, BotBulge = 0.8f, Lash = 0.05f, LashOuter = 1.2f, LowerLash = 0.022f, Iris = 0.227f, IrisScaleY = 1.3f, Pupil = 0.13f, RestLid = 0f },
                IrisHex = "#4A3072",
                Brow = new BrowShape { Id = "canon_yugi_muto", Thick = 0.035f, Arch = 0.07f, Tilt = 0.35f }, BrowPeak = 0.6f, BrowHex = "#3A2810",
                Nose = new NoseShape { Id = "canon_yugi_muto", Style = 0, Size = 0.55f, Shade = 0.3f },
                Mouth = new MouthShape { Id = "canon_yugi_muto", W = 0.18f, LipLine = 0.026f, LowerLip = 0.3f, RestCorner = -0.4f, OpenShape = 0.45f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "seto_kaiba", Name = "Seto Kaiba", Source = "Faces/seto_kaiba.png + series frames; unverified; conf 0.6",
                Eye = new EyeShape { Id = "canon_seto_kaiba", W = 0.42f, H = 0.2f, Tilt = 0.1f, Round = 0.7f, TopBulge = 1.3f, BotBulge = 0.86f, Lash = 0.05f, LashOuter = 1.3f, LowerLash = 0.011f, Iris = 0.27f, IrisScaleY = 1.09f, Pupil = 0.085f, RestLid = 0.2f },
                IrisHex = "#3B5976",
                Brow = new BrowShape { Id = "canon_seto_kaiba", Thick = 0.05f, Arch = 0.02f, Tilt = 0.45f }, BrowPeak = 0.5f, BrowHex = "#3F312B",
                Nose = new NoseShape { Id = "canon_seto_kaiba", Style = 1, Size = 0.7f, Shade = 0.5f },
                Mouth = new MouthShape { Id = "canon_seto_kaiba", W = 0.26f, LipLine = 0.033f, LowerLip = 0.6f, RestCorner = -0.09f, OpenShape = 0f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "joey_wheeler", Name = "Joey Wheeler", Source = "Faces/joey_wheeler.png + series frames; unverified; conf 0.65",
                Eye = new EyeShape { Id = "canon_joey_wheeler", W = 0.355f, H = 0.26f, Tilt = 0.03f, Round = 0.6f, TopBulge = 0.7f, BotBulge = 0.9f, Lash = 0.034f, LashOuter = 1.2f, LowerLash = 0.012f, Iris = 0.232f, IrisScaleY = 1f, Pupil = 0.071f, RestLid = 0.3f },
                IrisHex = "#696137",
                Brow = new BrowShape { Id = "canon_joey_wheeler", Thick = 0.07f, Arch = 0.02f, Tilt = 0.45f }, BrowPeak = 0.5f, BrowHex = "#514630",
                Nose = new NoseShape { Id = "canon_joey_wheeler", Style = 1, Size = 0.7f, Shade = 0.5f },
                Mouth = new MouthShape { Id = "canon_joey_wheeler", W = 0.2f, LipLine = 0.02f, LowerLip = 0.15f, RestCorner = 0.04f, OpenShape = 0.15f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "yami_yugi", Name = "Yami Yugi (the Pharaoh)", Source = "Faces/yami_yugi.png + series frames; unverified; conf 0.55",
                Eye = new EyeShape { Id = "canon_yami_yugi", W = 0.344f, H = 0.33f, Tilt = 0.09f, Round = 1f, TopBulge = 1.14f, BotBulge = 0.86f, Lash = 0.033f, LashOuter = 1f, LowerLash = 0.014f, Iris = 0.243f, IrisScaleY = 1.3f, Pupil = 0.13f, RestLid = 0.1f },
                IrisHex = "#4a345f",
                Brow = new BrowShape { Id = "canon_yami_yugi", Thick = 0.04f, Arch = 0.04f, Tilt = 0.45f }, BrowPeak = 0.5f, BrowHex = "#38362e",
                Nose = new NoseShape { Id = "canon_yami_yugi", Style = 2, Size = 0.75f, Shade = 0.4f },
                Mouth = new MouthShape { Id = "canon_yami_yugi", W = 0.18f, LipLine = 0.02f, LowerLip = 0.35f, RestCorner = 0f, OpenShape = 0.2f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "mai_valentine", Name = "Mai Valentine", Source = "Faces/mai_valentine.png + series frames; unverified; conf 0.72",
                Eye = new EyeShape { Id = "canon_mai_valentine", W = 0.3f, H = 0.19f, Tilt = 0.15f, Round = 0.55f, TopBulge = 0.6f, BotBulge = 0.9f, Lash = 0.03f, LashOuter = 2.2f, LowerLash = 0.014f, Iris = 0.145f, IrisScaleY = 1.3f, Pupil = 0.07f, RestLid = 0.3f },
                IrisHex = "#784370",
                Brow = new BrowShape { Id = "canon_mai_valentine", Thick = 0.04f, Arch = 0.08f, Tilt = 0.15f }, BrowPeak = 0.45f, BrowHex = "#573625",
                Nose = new NoseShape { Id = "canon_mai_valentine", Style = 2, Size = 0.6f, Shade = 0.4f },
                Mouth = new MouthShape { Id = "canon_mai_valentine", W = 0.18f, LipLine = 0.035f, LowerLip = 0.55f, RestCorner = -0.05f, OpenShape = 0.55f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "pegasus", Name = "Maximillion Pegasus", Source = "Faces/pegasus.png + series frames; unverified; conf 0.7",
                Eye = new EyeShape { Id = "canon_pegasus", W = 0.38f, H = 0.165f, Tilt = 0.07f, Round = 0.3f, TopBulge = 0.85f, BotBulge = 0.6f, Lash = 0.068f, LashOuter = 1f, LowerLash = 0.008f, Iris = 0.17f, IrisScaleY = 1f, Pupil = 0.063f, RestLid = 0.3f },
                IrisHex = "#714d38",
                Brow = new BrowShape { Id = "canon_pegasus", Thick = 0.05f, Arch = 0.12f, Tilt = 0.2f }, BrowPeak = 0.5f, BrowHex = "#454034",
                Nose = new NoseShape { Id = "canon_pegasus", Style = 2, Size = 0.7f, Shade = 0.4f },
                Mouth = new MouthShape { Id = "canon_pegasus", W = 0.3f, LipLine = 0.025f, LowerLip = 0.3f, RestCorner = 0.4f, OpenShape = 0.2f },
                HideEye = 1, KeepDecal = false },
            new() { FaceId = "yami_bakura", Name = "Yami Bakura (the Spirit of the Ring)", Source = "Faces/yami_bakura.png + series frames; unverified; conf 0.6",
                Eye = new EyeShape { Id = "canon_yami_bakura", W = 0.35f, H = 0.15f, Tilt = 0.06f, Round = 0.2f, TopBulge = 1.3f, BotBulge = 0.5f, Lash = 0.065f, LashOuter = 1.5f, LowerLash = 0.015f, Iris = 0.21f, IrisScaleY = 1.3f, Pupil = 0.09f, RestLid = 0.28f },
                IrisHex = "#856D57",
                Brow = new BrowShape { Id = "canon_yami_bakura", Thick = 0.05f, Arch = 0.04f, Tilt = 0.4f }, BrowPeak = 0.7f, BrowHex = "#2D2927",
                Nose = new NoseShape { Id = "canon_yami_bakura", Style = 1, Size = 0.65f, Shade = 0.4f },
                Mouth = new MouthShape { Id = "canon_yami_bakura", W = 0.23f, LipLine = 0.025f, LowerLip = 0.3f, RestCorner = 0.2f, OpenShape = 0.2f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "weevil", Name = "Weevil Underwood", Source = "Faces/weevil.png + series frames; unverified; conf 0.6",
                Eye = new EyeShape { Id = "canon_weevil", W = 0.45f, H = 0.21f, Tilt = 0.09f, Round = 0.15f, TopBulge = 0.6f, BotBulge = 0.9f, Lash = 0.045f, LashOuter = 1.5f, LowerLash = 0.01f, Iris = 0.13f, IrisScaleY = 1.3f, Pupil = 0.08f, RestLid = 0.15f },
                IrisHex = "#314078",
                Brow = new BrowShape { Id = "canon_weevil", Thick = 0.05f, Arch = 0.02f, Tilt = 0.35f }, BrowPeak = 0.25f, BrowHex = "#814F29",
                Nose = new NoseShape { Id = "canon_weevil", Style = 0, Size = 0.5f, Shade = 0.35f },
                Mouth = new MouthShape { Id = "canon_weevil", W = 0.27f, LipLine = 0.025f, LowerLip = 0.1f, RestCorner = 0.35f, OpenShape = 0.45f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "tristan", Name = "Tristan Taylor", Source = "Faces/tristan.png + series frames; unverified; conf 0.55",
                Eye = new EyeShape { Id = "canon_tristan", W = 0.38f, H = 0.19f, Tilt = 0.18f, Round = 0.3f, TopBulge = 0.6f, BotBulge = 0.9f, Lash = 0.034f, LashOuter = 1f, LowerLash = 0.01f, Iris = 0.165f, IrisScaleY = 1.3f, Pupil = 0.055f, RestLid = 0.25f },
                IrisHex = "#514F3B",
                Brow = new BrowShape { Id = "canon_tristan", Thick = 0.12f, Arch = 0.2f, Tilt = 0.45f }, BrowPeak = 0.1f, BrowHex = "#34322C",
                Nose = new NoseShape { Id = "canon_tristan", Style = 2, Size = 0.65f, Shade = 0.45f },
                Mouth = new MouthShape { Id = "canon_tristan", W = 0.23f, LipLine = 0.025f, LowerLip = 0.25f, RestCorner = -0.15f, OpenShape = 0.2f },
                HideEye = 0, KeepDecal = false },
            new() { FaceId = "rex_raptor", Name = "Rex Raptor", Source = "Faces/rex_raptor.png + series frames; unverified; conf 0.6",
                Eye = new EyeShape { Id = "canon_rex_raptor", W = 0.4f, H = 0.27f, Tilt = 0.18f, Round = 0.45f, TopBulge = 0.6f, BotBulge = 0.9f, Lash = 0.03f, LashOuter = 1.3f, LowerLash = 0.012f, Iris = 0.12f, IrisScaleY = 1.3f, Pupil = 0.06f, RestLid = 0f },
                IrisHex = "#2F2F2F",
                Brow = new BrowShape { Id = "canon_rex_raptor", Thick = 0.07f, Arch = 0.02f, Tilt = 0.45f }, BrowPeak = 0.25f, BrowHex = "#2C201B",
                Nose = new NoseShape { Id = "canon_rex_raptor", Style = 1, Size = 0.6f, Shade = 0.45f },
                Mouth = new MouthShape { Id = "canon_rex_raptor", W = 0.18f, LipLine = 0.02f, LowerLip = 0.1f, RestCorner = 0.2f, OpenShape = 0.2f },
                HideEye = 0, KeepDecal = false },
            // CANON_FACES_END
        };

        public static CharacterFace ForCharacter(string faceId)
        {
            if (string.IsNullOrEmpty(faceId)) return null;
            foreach (var c in Characters) if (c.FaceId == faceId) return c;
            return null;
        }

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
