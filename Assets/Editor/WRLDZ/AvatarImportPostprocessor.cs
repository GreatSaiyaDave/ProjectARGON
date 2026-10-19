using System.IO;
using UnityEditor;
using UnityEngine;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Import rules for the AR avatar rig (AVATAR_3D_INTEGRATION.md). The two shared base bodies and the opponent's
    /// duel clips must import as Humanoid so their avatars auto-generate: OverworldAvatar3d builds its pose graph on a
    /// humanoid Animator and ArOpponentDuelist reads bones via GetBoneTransform(HumanBodyBones.*) and animates with
    /// retargeted human-motion clips. Losing this rule is what let the rig fall back to a Generic import — no avatar,
    /// anim.isHuman == false — so the AI duelist's Build was silently skipped and the deploy cut-in never played.
    /// This postprocessor re-asserts the setting on every import so it can't drift again.
    ///
    /// Textures under Resources/WRLDZ/Avatar3D (2026-10-18, Meshy wardrobe): the body-hide maps (`hide_*`, `*_hide`)
    /// are data — linear, readable, no mips, uncompressed, point-filtered — or CombineHides reads garbage and the body
    /// shows through every garment; painted albedos (`albedo_*`) are sRGB and readable (GraftCloth samples the swatch
    /// halves of the flat shells) and capped at 2048. Existing .meta files already carried these flags; new pieces
    /// dropped in by the wardrobe tools get them here instead of by hand.
    ///
    /// Meshy sculpts (ids ending in _3d, Tools/avatar/meshy_wardrobe.py, owner 2026-10-18 "perfect them as they are"):
    /// their textures keep Meshy's 4096 px (`albedo_*_3d`, `Hair_*_3d`, `emission_*_3d`; `normal_*_3d` imports as a
    /// normal map) and their meshes import at full density, uncompressed, readable (OverworldAvatar3d.MorphFollow
    /// ties their vertices to the body's), with the sculpt's own normals and calculated tangents (the normal map).
    /// </summary>
    public sealed class AvatarImportPostprocessor : AssetPostprocessor
    {
        static readonly string[] HumanoidSet =
        {
            "Assets/Resources/WRLDZ/Avatar3D/argon_base_male.fbx",
            "Assets/Resources/WRLDZ/Avatar3D/argon_base_female.fbx",
            "Assets/Resources/WRLDZ/Avatar3D/ArgonClips_Duel.fbx",
            // Full character models (Meshy AI character output, yugi300274_work):
            "Assets/Resources/WRLDZ/Avatar3D/Characters/yugi.fbx",
        };

        const string AvatarRoot = "Assets/Resources/WRLDZ/Avatar3D/";

        static bool IsSculpt(string stem) => stem.Contains("_3d");

        void OnPreprocessModel()
        {
            foreach (var p in HumanoidSet)
            {
                if (assetPath != p) continue;
                var imp = (ModelImporter)assetImporter;
                if (imp.animationType != ModelImporterAnimationType.Human)
                {
                    imp.animationType = ModelImporterAnimationType.Human;
                    imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }
                // the two bases stay readable: tattoo patches are cut from the body mesh at runtime (Adornments) and the
                // sculpted pieces read the body's shape deltas (MorphFollow)
                if (p.Contains("argon_base_") && !imp.isReadable) imp.isReadable = true;
                return;
            }
            if (!assetPath.StartsWith(AvatarRoot)) return;
            var stem = Path.GetFileNameWithoutExtension(assetPath);
            // shoes and one-piece outfits stay readable: AvatarFootGrounding measures each sole (heel / toe contacts) from
            // the mesh worn on the foot, so the body stands on the shoe's sole, not the bare foot's (2026-10-18)
            if ((assetPath.Contains("/Clothes/") && (stem.StartsWith("argon_shoes_") || stem.StartsWith("argon_hat_") || stem.StartsWith("argon_face_")))
                || assetPath.Contains("/Outfits/") || assetPath.Contains("/Hair/"))
            {
                var mi = (ModelImporter)assetImporter;
                if (!mi.isReadable) mi.isReadable = true;
            }
            if (stem.StartsWith("argon_") && IsSculpt(stem))
            {
                var imp = (ModelImporter)assetImporter;
                imp.isReadable = true;
                imp.meshCompression = ModelImporterMeshCompression.Off;
                imp.importNormals = ModelImporterNormals.Import;
                imp.importTangents = ModelImporterTangents.CalculateMikk;
                imp.importBlendShapes = true;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;   // OverworldAvatar3d builds the material (AvatarRecolor / AvatarHair)
            }
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(AvatarRoot)) return;
            var imp = (TextureImporter)assetImporter;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            if (name.StartsWith("hide_") || name.EndsWith("_hide") || name.StartsWith("face_flat_"))
            {
                imp.sRGBTexture = false;
                imp.isReadable = true;
                imp.mipmapEnabled = false;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                var s = imp.GetDefaultPlatformTextureSettings();
                s.maxTextureSize = 2048;
                s.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SetPlatformTextureSettings(s);
                return;
            }
            if (IsSculpt(name) && (name.StartsWith("albedo_") || name.StartsWith("Hair_") || name.StartsWith("normal_") || name.StartsWith("emission_")))
            {
                // a sculpt's texture at Meshy's own resolution; the normal map as a normal map
                var normal = name.StartsWith("normal_");
                imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = !normal;
                imp.isReadable = false;
                var s = imp.GetDefaultPlatformTextureSettings();
                s.maxTextureSize = 4096;
                imp.SetPlatformTextureSettings(s);
                return;
            }
            if (name.StartsWith("albedo_") && assetPath.Contains("/Clothes/"))
            {
                imp.sRGBTexture = true;
                imp.isReadable = true;
                var s = imp.GetDefaultPlatformTextureSettings();
                if (s.maxTextureSize > 2048) { s.maxTextureSize = 2048; imp.SetPlatformTextureSettings(s); }
            }
        }
    }
}
