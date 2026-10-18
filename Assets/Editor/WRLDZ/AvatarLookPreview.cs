using System.IO;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Renders an avatar look to PNGs without Play mode (front, three-quarter, side) so wardrobe pieces can be checked
    /// on the real body with the real shaders, hide maps and hair clips. Run from the Pipeline eval:
    ///   return WRLDZ.EditorTools.AvatarLookPreview.Render("look:kaiba_dk");
    /// `what`: look:&lt;CharacterLooks id&gt; | npc:&lt;opponent name&gt; | player:male|female (base clothes)
    ///         | json:&lt;AvatarAppearance json&gt;. The 3D body is rendered even when the look names a 2D sprite set.
    /// Output: Temp/AvatarPreview/&lt;label&gt;_{front,q,side}.png (the paths are returned, one per line).
    /// </summary>
    public static class AvatarLookPreview
    {
        const int Layer = 30;

        public static string Render(string what, string outDir = "Temp/AvatarPreview", int w = 640, int h = 900, bool force3d = true)
        {
            var look = Resolve(what, out var label);
            if (look == null) return "no look for " + what;
            if (force3d) look.characterId = "";
            var root = new GameObject("AvatarLookPreview_" + label);
            root.transform.position = new Vector3(0f, -500f, 0f);   // far from the open scene's content
            Camera cam = null; RenderTexture rt = null;
            try
            {
                var av = OverworldAvatar3d.AttachWorld(root.transform, look, Layer);
                if (av == null) return "AttachWorld failed for " + label;
                av.FaceToward(Vector3.back);
                av.EvaluatePreview(0.016f);
                Directory.CreateDirectory(outDir);
                cam = new GameObject("AvatarLookPreviewCam").AddComponent<Camera>();
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
                cam.orthographic = true;
                // frame the renderers' bounds (the body faces +Z after FaceToward(Vector3.back))
                var bounds = new Bounds(root.transform.position, Vector3.zero); var first = true;
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                }
                var height = Mathf.Max(bounds.size.y, 0.5f);
                cam.orthographicSize = height * 0.56f;
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 20f;
                rt = new RenderTexture(w, h, 24);
                cam.targetTexture = rt;
                var centre = bounds.center;
                var sb = new StringBuilder();
                foreach (var (tag, yaw) in new[] { ("front", 0f), ("q", -40f), ("side", -90f) })
                {
                    var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    cam.transform.position = centre + dir * 4f;
                    cam.transform.LookAt(centre);
                    cam.Render();
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                    RenderTexture.active = prev;
                    var path = Path.Combine(outDir, label + "_" + tag + ".png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    sb.Append(path).Append('\n');
                }
                return sb.ToString();
            }
            finally
            {
                if (cam != null) { cam.targetTexture = null; Object.DestroyImmediate(cam.gameObject); }
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                Object.DestroyImmediate(root);
            }
        }

        static AvatarAppearance Resolve(string what, out string label)
        {
            label = "look";
            if (string.IsNullOrEmpty(what)) return null;
            var sep = what.IndexOf(':');
            var kind = sep < 0 ? what : what.Substring(0, sep);
            var arg = sep < 0 ? "" : what.Substring(sep + 1);
            switch (kind)
            {
                case "look":
                {
                    var look = CharacterLooks.Find(arg);
                    if (look == null) return null;
                    var a = AvatarAppearance.Default();
                    look.ApplyTo(a);
                    a.ClampToCatalog();
                    label = arg;
                    return a;
                }
                case "npc":
                {
                    var a = AiDuelistLooks.For(arg);
                    label = "npc_" + arg.ToLowerInvariant().Replace(' ', '_');
                    return a;
                }
                case "player":
                {
                    var a = AvatarAppearance.Default();
                    a.bodyType = arg == "female" ? 1 : 0;
                    a.ClampToCatalog();
                    label = "player_" + (arg == "female" ? "female" : "male");
                    return a;
                }
                case "json":
                {
                    var a = JsonUtility.FromJson<AvatarAppearance>(arg);
                    a?.ClampToCatalog();
                    label = "json";
                    return a;
                }
            }
            return null;
        }
    }
}
