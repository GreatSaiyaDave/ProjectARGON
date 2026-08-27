using System.IO;
using UnityEditor;
using UnityEngine;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Paper-doll rules: bottoms overlap the coat; hat/facial are accessory-only
    /// so the look (face) shows through.
    /// Batch: -executeMethod WRLDZ.EditorTools.ClothingLayerSmoke.RunBatch
    /// </summary>
    public static class ClothingLayerSmoke
    {
        [MenuItem("WRLDZ/Lab/Clothing Layer Smoke")]
        public static void RunInteractive()
        {
            var ok = RunAll(out var msg);
            EditorUtility.DisplayDialog(ok ? "Clothing smoke PASS" : "Clothing smoke FAIL", msg, "OK");
        }

        public static void RunBatch()
        {
            var ok = RunAll(out var msg);
            Debug.Log(ok ? "[WRLDZ CLOTHES] PASS " + msg : "[WRLDZ CLOTHES] FAIL " + msg);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool RunAll(out string msg)
        {
            if (!CheckBottomsCoverCoatShorts(out var bottomsMsg))
            {
                msg = bottomsMsg;
                return false;
            }

            if (!CheckAccessoryFaceHole("hat_cap.png", out var hatMsg))
            {
                msg = hatMsg;
                return false;
            }

            if (!CheckAccessoryFaceHole("hat_beanie.png", out hatMsg))
            {
                msg = hatMsg;
                return false;
            }

            if (!CheckLookSurvivesHat(out var lookMsg))
            {
                msg = lookMsg;
                return false;
            }

            if (!CheckHandsHaveNoShorts(out var handsMsg))
            {
                msg = handsMsg;
                return false;
            }

            msg = bottomsMsg + " | " + hatMsg + " | " + lookMsg + " | " + handsMsg;
            return true;
        }

        /// <summary>
        /// Pants draw on top of the shirt/coat. They must be opaque in the thigh
        /// band so they overlap the coat opening instead of sitting in a hole.
        /// </summary>
        public static bool CheckBottomsCoverCoatShorts() =>
            CheckBottomsCoverCoatShorts(out _);

        static bool CheckBottomsCoverCoatShorts(out string msg)
        {
            var tex = LoadClothes("bottoms_plain.png");
            if (tex == null)
            {
                msg = "missing bottoms_plain.png";
                return false;
            }

            var frac = OpaqueFrac(tex, 320, 400, PilY(tex, 730), PilY(tex, 670));
            Object.DestroyImmediate(tex);
            if (frac < 0.85f)
            {
                msg = $"bottoms thigh opaque {frac:P0}";
                return false;
            }

            msg = $"bottoms thigh opaque {frac:P0}";
            return true;
        }

        /// <summary>
        /// Face oval under a hat/facial accessory must be a hole, not a mannequin head.
        /// PIL y 270–360 / x 300–420 is the chin-to-eyes band under the brim.
        /// </summary>
        static bool CheckAccessoryFaceHole(string file, out string msg)
        {
            var tex = LoadClothes(file);
            if (tex == null)
            {
                msg = "missing " + file;
                return false;
            }

            var frac = OpaqueFrac(tex, 300, 420, PilY(tex, 360), PilY(tex, 270));
            Object.DestroyImmediate(tex);
            if (frac > 0.18f)
            {
                msg = file + $" face oval opaque {frac:P0} (expected a hole)";
                return false;
            }

            msg = file + $" face hole {1f - frac:P0} clear";
            return true;
        }

        static bool CheckLookSurvivesHat(out string msg)
        {
            var look = LoadClothes("look_0.png");
            var hat = LoadClothes("hat_cap.png");
            if (look == null || hat == null)
            {
                if (look != null) Object.DestroyImmediate(look);
                if (hat != null) Object.DestroyImmediate(hat);
                msg = "missing look_0 or hat_cap";
                return false;
            }

            var visible = 0;
            var face = 0;
            var x0 = 300;
            var x1 = 420;
            var y0 = PilY(look, 360);
            var y1 = PilY(look, 270);
            for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                if (look.GetPixel(x, y).a <= 0.30f) continue;
                face++;
                if (hat.GetPixel(x, y).a <= 0.30f) visible++;
            }

            Object.DestroyImmediate(look);
            Object.DestroyImmediate(hat);
            var frac = face > 0 ? visible / (float)face : 0f;
            if (frac < 0.75f)
            {
                msg = $"look face under cap {visible}/{face} ({frac:P0})";
                return false;
            }

            msg = $"look face under cap {visible}/{face}";
            return true;
        }

        static bool CheckHandsHaveNoShorts(out string msg)
        {
            var tex = LoadClothes("hands_gloves.png");
            if (tex == null)
            {
                msg = "missing hands_gloves.png";
                return false;
            }

            var frac = OpaqueFrac(tex, 310, 420, PilY(tex, 790), PilY(tex, 620));
            Object.DestroyImmediate(tex);
            if (frac > 0.08f)
            {
                msg = $"gloves torso leftovers {frac:P0}";
                return false;
            }

            msg = "gloves torso clear";
            return true;
        }

        static Texture2D LoadClothes(string file)
        {
            var path = Path.GetFullPath("Assets/StreamingAssets/WRLDZ/Avatar/clothes/" + file);
            if (!File.Exists(path))
            {
                Debug.LogError("[WRLDZ CLOTHES] missing " + path);
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(tex);
                Debug.LogError("[WRLDZ CLOTHES] could not decode " + file);
                return null;
            }

            return tex;
        }

        static int PilY(Texture2D tex, int pilY) => tex.height - pilY;

        static float OpaqueFrac(Texture2D tex, int x0, int x1, int y0, int y1)
        {
            if (y0 > y1)
            {
                var t = y0;
                y0 = y1;
                y1 = t;
            }

            var opaque = 0;
            var total = 0;
            for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                total++;
                if (tex.GetPixel(x, y).a > 0.16f) opaque++;
            }

            return total > 0 ? opaque / (float)total : 0f;
        }
    }
}
