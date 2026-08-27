using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
#if WRLDZ_HAS_ARFOUNDATION
using UnityEngine.XR.ARFoundation;
#endif

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// One-shot project wiring for full ARCore on the S23:
    /// ARCore loader on Android, AR background feature on URP renderers.
    /// </summary>
    public static class ArFoundationSetupMenu
    {
        const string MobileRenderer = "Assets/Settings/Mobile_Renderer.asset";
        const string PcRenderer = "Assets/Settings/PC_Renderer.asset";

        [InitializeOnLoadMethod]
        static void AutoWire()
        {
            EditorApplication.delayCall += () =>
            {
                try { EnsureAndroidArCoreLoader(); }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[WRLDZ ARCore] Auto loader: " + ex.Message);
                }

                try
                {
                    EnsureBackgroundFeature(MobileRenderer);
                    EnsureBackgroundFeature(PcRenderer);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[WRLDZ ARCore] Auto URP feature: " + ex.Message);
                }
            };
        }

        [MenuItem("WRLDZ/Lab/Full AR — Setup ARCore + URP")]
        public static void SetupNow()
        {
            EnsureAndroidArCoreLoader();
            EnsureBackgroundFeature(MobileRenderer);
            EnsureBackgroundFeature(PcRenderer);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog(
                "Full AR (ARCore)",
                "Android XR loader: ARCore (OpenXR stays for Quest).\n" +
                "URP: AR Background Renderer Feature on Mobile + PC renderers.\n\n" +
                "Build the S23 APK → ENTER AR → point at the floor.\n" +
                "See Assets/Scripts/WRLDZ/FULL_AR.md",
                "OK");
        }

        [MenuItem("WRLDZ/Lab/Open Full AR Notes")]
        public static void OpenNotes()
        {
            var path = "Assets/Scripts/WRLDZ/FULL_AR.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        const string XrPerTargetAsset = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        static void EnsureAndroidArCoreLoader()
        {
            EnsureXrManagerSettings(BuildTargetGroup.Android);
            EnsureXrManagerSettings(BuildTargetGroup.Standalone);
            var group = BuildTargetGroup.Android;
            BuildTargetGroupLoader(group, "UnityEngine.XR.ARCore.ARCoreLoader");
            // Keep OpenXR assigned as well so a Quest build can still find it.
            BuildTargetGroupLoader(group, "UnityEngine.XR.OpenXR.OpenXRLoader");
        }

        static void EnsureXrManagerSettings(BuildTargetGroup group)
        {
            var holder = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrPerTargetAsset);
            if (holder == null)
            {
                Debug.LogWarning("[WRLDZ ARCore] " + XrPerTargetAsset + " missing.");
                return;
            }

            if (!holder.HasManagerSettingsForBuildTarget(group))
                holder.CreateDefaultManagerSettingsForBuildTarget(group);
        }

        static void BuildTargetGroupLoader(BuildTargetGroup group, string loaderType)
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (settings == null)
            {
                Debug.LogWarning("[WRLDZ ARCore] XR General Settings missing for " + group +
                                 " — open Project Settings → XR Plug-in Management once, then rerun Full AR Setup.");
                return;
            }

            var mgr = settings.Manager;
            if (mgr == null) return;
            XRPackageMetadataStore.AssignLoader(mgr, loaderType, group);
            settings.InitManagerOnStart = true;
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(mgr);
            Debug.Log("[WRLDZ ARCore] Assigned " + loaderType + " on " + group);
        }

        static void EnsureBackgroundFeature(string rendererPath)
        {
#if !WRLDZ_HAS_ARFOUNDATION
            Debug.LogWarning("[WRLDZ ARCore] AR Foundation not loaded yet — exit Play Mode so packages resolve, then rerun.");
            return;
#else
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                Debug.LogWarning("[WRLDZ ARCore] Missing renderer " + rendererPath);
                return;
            }

            var existing = renderer.rendererFeatures;
            if (existing != null && existing.Any(f => f is ARBackgroundRendererFeature))
                return;

            var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
            feature.name = "ARBackgroundRendererFeature";
            AssetDatabase.AddObjectToAsset(feature, renderer);

            var so = new SerializedObject(renderer);
            var list = so.FindProperty("m_RendererFeatures");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            Debug.Log("[WRLDZ ARCore] AR Background feature → " + rendererPath);
#endif
        }
    }
}
