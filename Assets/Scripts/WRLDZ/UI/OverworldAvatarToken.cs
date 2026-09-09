using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// CC0 Quaternius Suit_Male / Suit_Female placeholder on the overworld token.
    /// 2D portrait stays as fallback when the OBJ is missing.
    /// </summary>
    public static class OverworldAvatarToken
    {
        public static void Attach(RectTransform slot, AvatarAppearance look)
        {
            if (slot == null) return;
            var existing = slot.Find("Avatar3d");
            if (existing != null)
                Object.Destroy(existing.gameObject);

            var female = look != null &&
                         ((look.bodyIndex % 2) == 1 ||
                          (look.portraitIndex % 2) == 1);
            var mesh = FreeModelCatalog.LoadCharacter(female ? "Suit_Female" : "Suit_Male")
                       ?? FreeModelCatalog.LoadCharacter(female ? "Ninja_Female" : "Ninja_Male");
            if (mesh == null) return;

            var go = new GameObject("Avatar3d", typeof(RectTransform), typeof(MeshFilter),
                typeof(MeshRenderer));
            go.transform.SetParent(slot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.15f, 0.08f);
            rt.anchorMax = new Vector2(0.85f, 0.95f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (sh != null)
            {
                var mat = new Material(sh);
                mat.color = new Color(0.82f, 0.78f, 0.72f, 1f);
                mr.sharedMaterial = mat;
            }

            go.transform.localRotation = Quaternion.Euler(12f, 180f, 0f);
            go.transform.localScale = Vector3.one * 90f;
        }
    }
}
