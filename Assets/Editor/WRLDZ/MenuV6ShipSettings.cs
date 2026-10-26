using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// The ONE ProjectSettings step of the menu build (BUILD_PLAN §5.6, §11 F6; out_p5/shaders_p5.json), run by the P5
    /// landing inside its slot after the refresh (never by editing the YAML while the Editor is open):
    /// <list type="number">
    /// <item>Always Included Shaders: the four WRLDZ/UI shaders reached only through Shader.Find (a pre-existing ship
    /// defect: a player build strips them) + the new WRLDZ/UI/MenuCelSmoke, by GUID, through SerializedObject on
    /// GraphicsSettings. Gated on <c>ShaderUtil.ShaderHasError</c> (not isSupported): if any of the five is missing,
    /// renamed or fails to compile, NOTHING is written. Idempotent: a shader already listed is not added again.
    /// About 3 min of main-thread shader compile, once.</item>
    /// <item>The Sprite Atlas mode → Sprite Atlas V2 (EditorSettings.asset m_SpritePackerMode; today 0 = disabled), in the
    /// same slot so the shader compile and any atlas work happen once. No atlas asset is created here (decision: the
    /// per-group atlases wait for the Profiler's batch count, PROGRESS_p5code).</item>
    /// </list>
    /// <see cref="DryRun"/> changes nothing and reports what <see cref="Apply"/> would do. Both write a report file when given
    /// a path (the landing reads it; no dialog, no Exit).
    /// </summary>
    public static class MenuV6ShipSettings
    {
        public const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";

        /// <summary>The five shaders: name, GUID (their .meta files; MenuCelSmoke's reserved in out_p5/shaders_p5.json).</summary>
        public static readonly (string name, string guid)[] Shaders =
        {
            ("WRLDZ/UI/ArcaneEnergyFlow", "7ad2687e2447801e09db59c57c1000d7"),
            ("WRLDZ/UI/BarrierStoneSeal", "89ea08c5c0373ec0096554086da6a04f"),
            ("WRLDZ/UI/HieroglyphGlow", "5d0fd3ba49b25390f9c6fb8f6b44cc58"),
            ("WRLDZ/UI/SolidVisionSlate", "287b331cbbd4af16f88042269491a258"),
            ("WRLDZ/UI/MenuCelSmoke", "7dd18a7a3605b05b93a72e5b40bb64cc"),
        };

        public static string DryRun(string reportPath = null) => Run(false, reportPath);

        public static string Apply(string reportPath = null) => Run(true, reportPath);

        /// <summary>
        /// The landing's rollback of this step (Editor-side, like the step itself): removes the five shaders' entries from
        /// m_AlwaysIncludedShaders by GUID (the seven built-ins stay) and, when <paramref name="restorePackerDisabled"/>, puts
        /// the sprite packer back to Disabled if it is Sprite Atlas V2. Idempotent.
        /// </summary>
        public static string Revert(bool restorePackerDisabled, string reportPath = null)
        {
            var sb = new StringBuilder("revert " + DateTime.Now.ToString("s") + "\n");
            var ok = true;
            try
            {
                var all = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath);
                var gs = all != null && all.Length > 0 ? all[0] : null;
                if (gs == null) throw new Exception(GraphicsSettingsPath + " did not load");
                var so = new SerializedObject(gs);
                var arr = so.FindProperty("m_AlwaysIncludedShaders");
                var guids = new HashSet<string>();
                foreach (var (_, guid) in Shaders) guids.Add(guid);
                var removed = 0;
                for (var i = arr.arraySize - 1; i >= 0; i--)
                {
                    var o = arr.GetArrayElementAtIndex(i).objectReferenceValue;
                    var path = o != null ? AssetDatabase.GetAssetPath(o) : null;
                    var g = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
                    if (g == null || !guids.Contains(g)) continue;
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = null;   // clear first, then delete the slot
                    arr.DeleteArrayElementAtIndex(i);
                    removed++;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                sb.AppendLine("  always included: removed " + removed + ", " + arr.arraySize + " left");
                if (restorePackerDisabled && EditorSettings.spritePackerMode == SpritePackerMode.SpriteAtlasV2)
                {
                    EditorSettings.spritePackerMode = SpritePackerMode.Disabled;
                    sb.AppendLine("  sprite packer mode -> Disabled");
                }

                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                ok = false;
                sb.AppendLine("  FAIL " + ex.Message);
            }

            sb.Insert(0, (ok ? "OK" : "FAIL") + "  MenuV6ShipSettings ");
            var report = sb.ToString();
            if (!string.IsNullOrEmpty(reportPath))
            {
                try { File.WriteAllText(reportPath, report); }
                catch (Exception ex) { Debug.LogWarning("[WRLDZ] MenuV6ShipSettings report: " + ex.Message); }
            }

            Debug.Log("[WRLDZ] " + report);
            return report;
        }

        static string Run(bool write, string reportPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine((write ? "apply" : "dry run") + " " + DateTime.Now.ToString("s"));
            var ok = true;
            var shaders = new List<Shader>();
            foreach (var (name, guid) in Shaders)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sh = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (sh == null)
                {
                    sb.AppendLine("  FAIL " + name + ": no shader asset for guid " + guid + " (" + (string.IsNullOrEmpty(path) ? "unknown guid" : path) + ")");
                    ok = false;
                    continue;
                }

                if (sh.name != name)
                {
                    sb.AppendLine("  FAIL " + guid + ": is '" + sh.name + "', expected '" + name + "'");
                    ok = false;
                    continue;
                }

                if (ShaderUtil.ShaderHasError(sh))
                {
                    sb.AppendLine("  FAIL " + name + ": ShaderUtil.ShaderHasError");
                    try
                    {
                        foreach (var m in ShaderUtil.GetShaderMessages(sh))
                            sb.AppendLine("       " + m.severity + " " + m.message + " (" + m.file + ":" + m.line + ")");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("       (messages: " + ex.Message + ")");
                    }

                    ok = false;
                    continue;
                }

                sb.AppendLine("  ok   " + name + " (" + path + ")");
                shaders.Add(sh);
            }

            UnityEngine.Object gs = null;
            try
            {
                var all = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath);
                gs = all != null && all.Length > 0 ? all[0] : null;
            }
            catch (Exception ex)
            {
                sb.AppendLine("  FAIL GraphicsSettings: " + ex.Message);
            }

            if (gs == null)
            {
                sb.AppendLine("  FAIL " + GraphicsSettingsPath + " did not load");
                ok = false;
            }

            var added = new List<string>();
            var listed = new List<string>();
            if (gs != null)
            {
                var so = new SerializedObject(gs);
                var arr = so.FindProperty("m_AlwaysIncludedShaders");
                if (arr == null || !arr.isArray)
                {
                    sb.AppendLine("  FAIL m_AlwaysIncludedShaders not found");
                    ok = false;
                }
                else
                {
                    var have = new HashSet<Shader>();
                    for (var i = 0; i < arr.arraySize; i++)
                    {
                        var listed1 = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                        if (listed1 != null) have.Add(listed1);
                    }
                    foreach (var sh in shaders)
                    {
                        if (have.Contains(sh)) listed.Add(sh.name);
                        else added.Add(sh.name);
                    }

                    sb.AppendLine("  always included: " + arr.arraySize + " entries; already listed " + listed.Count + ", to add " + added.Count);
                    if (write && ok && added.Count > 0)
                    {
                        foreach (var sh in shaders)
                        {
                            if (have.Contains(sh)) continue;
                            arr.InsertArrayElementAtIndex(arr.arraySize);
                            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                        }

                        so.ApplyModifiedPropertiesWithoutUndo();
                        sb.AppendLine("  added: " + string.Join(", ", added));
                    }
                }
            }

            // the Sprite Atlas mode (V2), only from Disabled (a project that already picked a mode keeps it)
            var mode = EditorSettings.spritePackerMode;
            sb.AppendLine("  sprite packer mode: " + mode);
            var wantV2 = mode == SpritePackerMode.Disabled;
            if (write && ok && wantV2)
            {
                EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
                sb.AppendLine("  sprite packer mode -> " + EditorSettings.spritePackerMode);
            }
            else if (!wantV2) sb.AppendLine("  sprite packer mode kept");

            if (write && ok) AssetDatabase.SaveAssets();
            sb.Insert(0, (ok ? "OK" : "FAIL (nothing written)") + "  MenuV6ShipSettings ");
            var report = sb.ToString();
            if (!string.IsNullOrEmpty(reportPath))
            {
                try
                {
                    var dir = Path.GetDirectoryName(reportPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(reportPath, report);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] MenuV6ShipSettings report: " + ex.Message);
                }
            }

            Debug.Log("[WRLDZ] " + report);
            return report;
        }
    }
}
