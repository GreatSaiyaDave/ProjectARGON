using System;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// A rigid prop hung on a bone: earrings, wristbands, belts, badges, pendants. The model is a prefab / FBX under
    /// Resources (e.g. "WRLDZ/Avatar3D/Accessories/&lt;id&gt;"); OverworldAvatar3d parents an instance to the bone with the
    /// local pose below, so it rides the animation and the stature scale. A flat colour (Hex) covers models that ship
    /// without textures (3D-print downloads).
    /// </summary>
    [Serializable]
    public sealed class AccessoryItem
    {
        public string Id;
        public string Name;
        public string Blurb;
        public HumanBodyBones Bone = HumanBodyBones.Chest;
        public Vector3 LocalPos;
        public Vector3 LocalEuler;
        public float Scale = 1f;
        public string Resource;
        public string Hex;
        /// <summary>-1 any body, 0 male, 1 female.</summary>
        public int BodyType = -1;
        public int PriceDigi;

        public bool FitsBody(bool female) => BodyType < 0 || BodyType == (female ? 1 : 0);
    }

    /// <summary>
    /// A tattoo: an alpha PNG under Resources ("WRLDZ/Avatar3D/Tattoos/&lt;id&gt;") stamped on the skin round an anchor given
    /// in a bone's space. OverworldAvatar3d copies the body triangles round the anchor into a skinned patch (same bones,
    /// same blend shapes, 1.5 mm off the skin) and projects the artwork onto it, so the ink follows the limb through
    /// animation, the weight / build / face sliders and the stature scale, on either body, with nothing to clip.
    /// </summary>
    [Serializable]
    public sealed class TattooItem
    {
        public string Id;
        public string Name;
        public string Blurb;
        public HumanBodyBones Bone = HumanBodyBones.Head;
        /// <summary>Anchor on the skin, in the bone's space (metres, bind pose).</summary>
        public Vector3 LocalPos;
        /// <summary>Width of the artwork on the skin (m).</summary>
        public float Size = 0.08f;
        /// <summary>Spin of the artwork round the skin normal (degrees).</summary>
        public float RotationDeg;
        public string Resource;
        public int BodyType = -1;
        public int PriceDigi;

        public bool FitsBody(bool female) => BodyType < 0 || BodyType == (female ? 1 : 0);
    }

    /// <summary>
    /// Accessories and tattoos the customizer offers. Sourced art only (owner rule: nothing invented): an entry is
    /// added together with its artwork / model. Recipe: AVATAR_3D_INTEGRATION.md, 2026-10-18 addendum.
    /// </summary>
    public static class AdornmentCatalog
    {
        public static readonly AccessoryItem[] Accessories =
        {
        };

        public static readonly TattooItem[] Tattoos =
        {
        };

        public static AccessoryItem Accessory(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < Accessories.Length; i++) if (Accessories[i].Id == id) return Accessories[i];
            return null;
        }

        public static TattooItem Tattoo(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < Tattoos.Length; i++) if (Tattoos[i].Id == id) return Tattoos[i];
            return null;
        }
    }
}
