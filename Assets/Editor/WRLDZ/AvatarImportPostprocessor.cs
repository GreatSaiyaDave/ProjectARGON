using UnityEditor;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Import rules for the AR avatar rig (AVATAR_3D_INTEGRATION.md). The two shared base bodies and the opponent's
    /// duel clips must import as Humanoid so their avatars auto-generate: OverworldAvatar3d builds its pose graph on a
    /// humanoid Animator and ArOpponentDuelist reads bones via GetBoneTransform(HumanBodyBones.*) and animates with
    /// retargeted human-motion clips. Losing this rule is what let the rig fall back to a Generic import — no avatar,
    /// anim.isHuman == false — so the AI duelist's Build was silently skipped and the deploy cut-in never played.
    /// This postprocessor re-asserts the setting on every import so it can't drift again.
    /// </summary>
    public sealed class AvatarImportPostprocessor : AssetPostprocessor
    {
        static readonly string[] HumanoidSet =
        {
            "Assets/Resources/WRLDZ/Avatar3D/argon_base_male.fbx",
            "Assets/Resources/WRLDZ/Avatar3D/argon_base_female.fbx",
            "Assets/Resources/WRLDZ/Avatar3D/ArgonClips_Duel.fbx",
        };

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
                return;
            }
        }
    }
}