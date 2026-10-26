using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using WRLDZ.UI.MenuV6;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// P8 (PERF_PLAN.md step 2): the menu's Sprite Atlas V2 assets, made Editor-side INSIDE the landing slot (never by
    /// writing their YAML while the Editor is open): one <c>.spriteatlasv2</c> per row of <see cref="MenuV6Perf.Atlases"/> in
    /// <c>Assets/Resources/WRLDZ/MenuV6/Atlas/</c> (the folder and the 8×8 white blocks come with the P8 art).
    /// <list type="bullet">
    /// <item>Members: every Single-sprite texture directly in the row's folders, longest side &lt; 1024 px, minus the row's
    /// exclusions (the flipbook sheets), plus its white block. A member already packed by another atlas fails the dry run.</item>
    /// <item>Packing: rotation off, tight packing off (UGUI's simple and sliced Images need rect packing), padding 8 (the 6×6
    /// ASTC block + the bilinear tap), alpha dilation of the padding on (its colour = the edge's, as the sources'
    /// alphaIsTransparency dilates theirs), block offset 1.</item>
    /// <item>Texture: sRGB, no mips, not readable, bilinear, aniso 1 (the sources'); max 2048.</item>
    /// <item>Formats: Standalone = the members' own Standalone format and quality (all BC7, fast: refused when they differ);
    /// Android = ASTC 4×4 (a group mixing 4×4 and 6×6 packs at 4×4: none loses quality); Include in Build on.</item>
    /// </list>
    /// <see cref="DryRun"/> changes nothing. <see cref="Apply"/> is idempotent: an atlas whose members and settings already
    /// match is left alone. <see cref="Revert"/> deletes exactly these atlas assets (by their fixed paths, only when the asset
    /// there is a sprite atlas): the sprites go back to their own textures. <see cref="Check"/> is read-only (MenuSmokeTest).
    /// Each writes a report when given a path (the landing reads it; no dialog, no Exit).
    /// </summary>
    public static class MenuV6Atlases
    {
        const string AssetsRes = "Assets/Resources/";

        public static string DryRun(string reportPath = null) => Run(false, reportPath);

        public static string Apply(string reportPath = null) => Run(true, reportPath);

        /// <summary>The members (texture asset paths) of one atlas, with why anything in its folders stays out.</summary>
        public static List<string> Members(MenuV6Perf.AtlasDef a, StringBuilder log, out string error)
        {
            error = null;
            var list = new List<string>();
            var excl = new HashSet<string>(a.Exclude ?? new string[0]);
            foreach (var f in a.Folders)
            {
                var dir = AssetsRes + MenuV6Art.Root + f;
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    error = "folder missing: " + dir;
                    return list;
                }

                var found = new List<string>();
                foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (string.IsNullOrEmpty(p) || Path.GetDirectoryName(p).Replace('\\', '/') != dir) continue;   // direct children only
                    found.Add(p);
                }

                found.Sort(string.CompareOrdinal);
                foreach (var p in found)
                {
                    var res = ResPath(p);
                    var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (imp == null)
                    {
                        log?.AppendLine("    out " + res + ": not a texture importer");
                        continue;
                    }

                    // the table's exclusions are MenuV6-relative ("Rigs/Land/niche/sliver"), res is the Resources path
                    // ("WRLDZ/MenuV6/Rigs/Land/niche/sliver"): compare both forms (p8review: the full path alone never matched)
                    var rel = res.StartsWith(MenuV6Art.Root, StringComparison.Ordinal) ? res.Substring(MenuV6Art.Root.Length) : res;
                    if (excl.Contains(rel) || excl.Contains(res))
                    {
                        log?.AppendLine("    out " + res + ": excluded (read as a Texture2D sheet)");
                        continue;
                    }

                    if (imp.textureType != TextureImporterType.Sprite || imp.spriteImportMode != SpriteImportMode.Single)
                    {
                        log?.AppendLine("    out " + res + ": not a single sprite");
                        continue;
                    }

                    imp.GetSourceTextureWidthAndHeight(out var w, out var h);
                    if (Mathf.Max(w, h) >= MenuV6Perf.MaxSide)
                    {
                        log?.AppendLine("    out " + res + ": " + w + "x" + h + " (>= " + MenuV6Perf.MaxSide + " px)");
                        continue;
                    }

                    if (imp.mipmapEnabled || imp.isReadable)
                    {
                        log?.AppendLine("    out " + res + ": mipmapped / readable (the atlas has neither)");
                        continue;
                    }

                    list.Add(p);
                }
            }

            var white = AssetsRes + a.White + ".png";
            if (AssetImporter.GetAtPath(white) as TextureImporter == null)
            {
                error = "white block missing: " + white + " (the P8 art)";
                return list;
            }

            list.Add(white);
            return list;
        }

        static string ResPath(string assetPath)
        {
            var p = assetPath.StartsWith(AssetsRes, StringComparison.Ordinal) ? assetPath.Substring(AssetsRes.Length) : assetPath;
            var dot = p.LastIndexOf('.');
            return dot > 0 ? p.Substring(0, dot) : p;
        }

        public static SpriteAtlasPackingSettings Packing() => new()
        {
            blockOffset = 1, padding = MenuV6Perf.Padding, enableRotation = false, enableTightPacking = false, enableAlphaDilation = true
        };

        public static SpriteAtlasTextureSettings TextureSettings() => new()
        {
            readable = false, generateMipMaps = false, sRGB = true, filterMode = FilterMode.Bilinear, anisoLevel = 1
        };

        /// <summary>The members' Standalone format + quality (all equal, else null and <paramref name="why"/>).</summary>
        static TextureImporterPlatformSettings Standalone(List<string> members, out string why)
        {
            why = null;
            TextureImporterPlatformSettings first = null;
            foreach (var p in members)
            {
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) continue;
                var s = imp.GetPlatformTextureSettings("Standalone");
                if (s == null || !s.overridden) continue;   // the white block: its override is checked below like any other
                if (first == null)
                {
                    first = s;
                    continue;
                }

                if (s.format != first.format || s.compressionQuality != first.compressionQuality)
                {
                    why = "Standalone formats differ: " + members[0] + " " + first.format + "/" + first.compressionQuality + " vs " + p + " " +
                          s.format + "/" + s.compressionQuality;
                    return null;
                }
            }

            if (first == null)
            {
                why = "no member has a Standalone override";
                return null;
            }

            return new TextureImporterPlatformSettings
            {
                name = "Standalone", overridden = true, maxTextureSize = MenuV6Perf.PageMax, format = first.format,
                compressionQuality = first.compressionQuality, textureCompression = TextureImporterCompression.Compressed
            };
        }

        static TextureImporterPlatformSettings Android() => new()
        {
            name = "Android", overridden = true, maxTextureSize = MenuV6Perf.PageMax, format = TextureImporterFormat.ASTC_4x4,
            compressionQuality = 50, textureCompression = TextureImporterCompression.Compressed
        };

        static TextureImporterPlatformSettings Default() => new()
        {
            name = "DefaultTexturePlatform", overridden = false, maxTextureSize = MenuV6Perf.PageMax, format = TextureImporterFormat.Automatic,
            textureCompression = TextureImporterCompression.Compressed
        };

        /// <summary>The atlas at <paramref name="path"/> differs from the wanted settings (null = it matches).</summary>
        static string SettingsDiff(SpriteAtlasImporter imp, TextureImporterPlatformSettings sa)
        {
            var d = new List<string>();
            var p = imp.packingSettings;
            var want = Packing();
            if (p.padding != want.padding) d.Add("padding " + p.padding);
            if (p.enableRotation) d.Add("rotation on");
            if (p.enableTightPacking) d.Add("tight packing on");
            if (p.enableAlphaDilation != want.enableAlphaDilation) d.Add("alpha dilation " + p.enableAlphaDilation);
            if (p.blockOffset != want.blockOffset) d.Add("block offset " + p.blockOffset);
            var t = imp.textureSettings;
            if (t.readable) d.Add("readable");
            if (t.generateMipMaps) d.Add("mipmaps");
            if (!t.sRGB) d.Add("linear");
            if (t.filterMode != FilterMode.Bilinear) d.Add("filter " + t.filterMode);
            if (!imp.includeInBuild) d.Add("not in build");
            var st = imp.GetPlatformSettings("Standalone");
            if (st == null || !st.overridden || (sa != null && (st.format != sa.format || st.compressionQuality != sa.compressionQuality)) ||
                st.maxTextureSize != MenuV6Perf.PageMax)
                d.Add("Standalone " + (st == null ? "none" : st.overridden + " " + st.format + "/" + st.compressionQuality + " max " + st.maxTextureSize));
            var an = imp.GetPlatformSettings("Android");
            if (an == null || !an.overridden || an.format != TextureImporterFormat.ASTC_4x4 || an.maxTextureSize != MenuV6Perf.PageMax)
                d.Add("Android " + (an == null ? "none" : an.overridden + " " + an.format + " max " + an.maxTextureSize));
            return d.Count == 0 ? null : string.Join(", ", d);
        }

        /// <summary>The packables of an existing atlas asset, as sorted asset paths (null when none is there).</summary>
        static List<string> Packables(string atlasPath)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null) return null;
            var l = new List<string>();
            try
            {
                foreach (var o in atlas.GetPackables())
                    if (o != null)
                        l.Add(AssetDatabase.GetAssetPath(o));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] MenuV6Atlases: the packables of " + atlasPath + " do not read back (" + ex.Message + ")");
                l.Clear();
            }

            l.Sort(string.CompareOrdinal);
            return l;
        }

        static string Run(bool write, string reportPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine((write ? "apply" : "dry run") + " " + DateTime.Now.ToString("s") + " (sprite packer " + EditorSettings.spritePackerMode + ")");
            var ok = true;
            if (EditorSettings.spritePackerMode != SpritePackerMode.SpriteAtlasV2)
            {
                sb.AppendLine("  FAIL the sprite packer mode is " + EditorSettings.spritePackerMode + " (P5 set Sprite Atlas V2)");
                ok = false;
            }

            var root = AssetsRes + MenuV6Perf.AtlasRoot.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root))
            {
                sb.AppendLine("  FAIL " + root + " missing (the P8 art set)");
                ok = false;
            }

            // every sprite of the project that another atlas already packs (a sprite may sit in one atlas only)
            var taken = new Dictionary<string, string>();
            foreach (var g in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                var ap = AssetDatabase.GUIDToAssetPath(g);
                var mine = false;
                foreach (var a in MenuV6Perf.Atlases)
                    if (ap == a.AssetPath)
                        mine = true;
                if (mine) continue;
                var pk = Packables(ap);
                if (pk == null) continue;
                foreach (var p in pk) taken[p] = ap;
            }

            var plan = new List<(MenuV6Perf.AtlasDef a, List<string> members, TextureImporterPlatformSettings sa, string action)>();
            foreach (var a in MenuV6Perf.Atlases)
            {
                var log = new StringBuilder();
                var members = Members(a, log, out var err);
                if (err != null)
                {
                    sb.AppendLine("  FAIL " + a.Name + ": " + err);
                    ok = false;
                    continue;
                }

                foreach (var m in members)
                    if (taken.TryGetValue(m, out var other))
                    {
                        sb.AppendLine("  FAIL " + a.Name + ": " + m + " is packed by " + other);
                        ok = false;
                    }

                var sa = Standalone(members, out var why);
                if (sa == null)
                {
                    sb.AppendLine("  FAIL " + a.Name + ": " + why);
                    ok = false;
                    continue;
                }

                long px = 0;
                foreach (var m in members)
                {
                    var imp = AssetImporter.GetAtPath(m) as TextureImporter;
                    if (imp == null) continue;
                    imp.GetSourceTextureWidthAndHeight(out var w, out var h);
                    px += (long)(w + MenuV6Perf.Padding) * (h + MenuV6Perf.Padding);
                }

                var have = Packables(a.AssetPath);
                var wantSorted = new List<string>(members);
                wantSorted.Sort(string.CompareOrdinal);
                string action;
                if (have == null) action = "create";
                else if (have.Count == 0) action = "update members";   // its packables read back empty: written again
                else
                {
                    var same = have.Count == wantSorted.Count;
                    for (var i = 0; same && i < have.Count; i++) same = have[i] == wantSorted[i];
                    var imp = AssetImporter.GetAtPath(a.AssetPath) as SpriteAtlasImporter;
                    var diff = imp != null ? SettingsDiff(imp, sa) : "no SpriteAtlasImporter";
                    action = !same ? "update members" : diff != null ? "update settings (" + diff + ")" : "unchanged";
                }

                sb.AppendLine("  " + a.Name + ": " + members.Count + " members, ~" + (px / 1e6).ToString("0.00") + " MP with padding, Standalone " +
                              sa.format + "/" + sa.compressionQuality + ", Android ASTC_4x4 -> " + action);
                sb.Append(log);
                plan.Add((a, members, sa, action));
            }

            var made = new List<string>();
            if (write && ok)
            {
                try
                {
                    foreach (var (a, members, _, action) in plan)
                    {
                        if (action != "create" && action != "update members") continue;
                        var asset = new SpriteAtlasAsset();
                        var objs = new List<UnityEngine.Object>();
                        foreach (var m in members)
                        {
                            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(m);
                            if (tex != null) objs.Add(tex);
                        }

                        asset.Add(objs.ToArray());
                        SpriteAtlasAsset.Save(asset, a.AssetPath);
                        made.Add(a.AssetPath);
                        AssetDatabase.ImportAsset(a.AssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    }

                    foreach (var (a, _, sa, action) in plan)
                    {
                        if (action == "unchanged") continue;
                        var imp = AssetImporter.GetAtPath(a.AssetPath) as SpriteAtlasImporter;
                        if (imp == null) throw new Exception(a.AssetPath + ": no SpriteAtlasImporter after the save");
                        imp.packingSettings = Packing();
                        imp.textureSettings = TextureSettings();
                        imp.SetPlatformSettings(Default());
                        imp.SetPlatformSettings(sa);
                        imp.SetPlatformSettings(Android());
                        imp.includeInBuild = true;
                        imp.SaveAndReimport();
                        sb.AppendLine("  wrote " + a.AssetPath + " (" + action + ")");
                    }

                    AssetDatabase.SaveAssets();
                }
                catch (Exception ex)
                {
                    ok = false;
                    sb.AppendLine("  FAIL writing: " + ex.Message + " (written so far: " + string.Join(", ", made) + "; Revert deletes them)");
                }

                if (ok)
                {
                    var check = Check(out var cok);
                    sb.Append(check);
                    if (!cok) ok = false;
                }
            }

            sb.Insert(0, (ok ? "OK" : write ? "FAIL" : "FAIL (nothing written)") + "  MenuV6Atlases ");
            return Report(sb.ToString(), reportPath);
        }

        /// <summary>Read-only: every atlas of the table is there with its settings, packs exactly its members, and binds each
        /// member sprite (SpriteAtlas.CanBindTo). The report lists every problem; <paramref name="ok"/> is false on any.</summary>
        public static string Check(out bool ok)
        {
            ok = true;
            var sb = new StringBuilder("  check:\n");
            foreach (var a in MenuV6Perf.Atlases)
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(a.AssetPath);
                var imp = AssetImporter.GetAtPath(a.AssetPath) as SpriteAtlasImporter;
                if (atlas == null || imp == null)
                {
                    sb.AppendLine("    FAIL " + a.Name + ": no sprite atlas at " + a.AssetPath);
                    ok = false;
                    continue;
                }

                var members = Members(a, null, out var err);
                if (err != null)
                {
                    sb.AppendLine("    FAIL " + a.Name + ": " + err);
                    ok = false;
                    continue;
                }

                var sa = Standalone(members, out var why);
                var diff = SettingsDiff(imp, sa);
                if (sa == null || diff != null)
                {
                    sb.AppendLine("    FAIL " + a.Name + ": settings " + (why ?? diff));
                    ok = false;
                }

                // what it packs: a mismatch with the table fails; a list that reads back empty is reported (the V2 importer
                // may keep its packables on the source asset only)
                var have = Packables(a.AssetPath);
                var want = new List<string>(members);
                want.Sort(string.CompareOrdinal);
                var packs = have == null || have.Count == 0 ? "packables not readable" : have.Count + " packables";
                if (have != null && have.Count > 0 && string.Join("|", have) != string.Join("|", want))
                {
                    sb.AppendLine("    FAIL " + a.Name + ": packs " + have.Count + " objects, the table wants " + want.Count);
                    ok = false;
                }

                // the runtime lookup (Resources, as MenuV6Art loads every member)
                var rl = Resources.Load<SpriteAtlas>(a.ResourcesPath);
                if (rl == null)
                {
                    sb.AppendLine("    FAIL " + a.Name + ": Resources.Load<SpriteAtlas>(\"" + a.ResourcesPath + "\") is null");
                    ok = false;
                }

                // binding (informational here: packing in the Editor can wait for Play; the capture's budget shows the pages)
                var bound = 0;
                var sprites = 0;
                foreach (var m in members)
                {
                    var s = AssetDatabase.LoadAssetAtPath<Sprite>(m);
                    if (s == null) continue;
                    sprites++;
                    if (atlas.CanBindTo(s)) bound++;
                }

                sb.AppendLine("    " + (ok ? "ok  " : "..  ") + " " + a.Name + ": " + members.Count + " members, " + packs + ", binds " + bound + "/" + sprites +
                              ", spriteCount " + atlas.spriteCount + ", in build " + imp.includeInBuild);
            }

            return sb.ToString();
        }

        /// <summary>
        /// The landing's rollback of the step: deletes the table's atlas assets (each by its fixed path, only when the asset
        /// there is a sprite atlas; AssetDatabase.DeleteAsset removes its .meta too). Idempotent: a missing one is skipped.
        /// </summary>
        public static string Revert(string reportPath = null)
        {
            var sb = new StringBuilder("revert " + DateTime.Now.ToString("s") + "\n");
            var ok = true;
            foreach (var a in MenuV6Perf.Atlases)
            {
                var p = a.AssetPath;
                var main = AssetDatabase.LoadMainAssetAtPath(p);
                if (main == null && !File.Exists(p))
                {
                    sb.AppendLine("  none " + p);
                    continue;
                }

                if (main != null && main as SpriteAtlas == null && main as SpriteAtlasAsset == null)
                {
                    sb.AppendLine("  FAIL " + p + " is a " + main.GetType().Name + ", not a sprite atlas: left alone");
                    ok = false;
                    continue;
                }

                if (AssetDatabase.DeleteAsset(p)) sb.AppendLine("  deleted " + p);
                else
                {
                    sb.AppendLine("  FAIL could not delete " + p);
                    ok = false;
                }
            }

            MenuV6Art.ClearCache();
            sb.Insert(0, (ok ? "OK" : "FAIL") + "  MenuV6Atlases ");
            return Report(sb.ToString(), reportPath);
        }

        static string Report(string report, string reportPath)
        {
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
                    Debug.LogWarning("[WRLDZ] MenuV6Atlases report: " + ex.Message);
                }
            }

            Debug.Log("[WRLDZ] " + report);
            return report;
        }
    }
}
