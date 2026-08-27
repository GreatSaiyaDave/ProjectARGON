using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WRLDZ.UI
{
    /// <summary>
    /// Shared runtime UI helpers. Unity 6 removed Arial.ttf as a builtin font;
    /// menus must use LegacyRuntime.ttf or project fonts.
    /// </summary>
    public static class UiFoundation
    {
        static Font _font;
        static Sprite _whiteSprite;

        /// <summary>Safe builtin UI font for Unity 6+ (never request Arial.ttf).</summary>
        public static Font BuiltinFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
                Debug.LogError("[WRLDZ] LegacyRuntime.ttf builtin font missing — UI text will be blank.");
            return _font;
        }

        /// <summary>
        /// 1×1 white sprite so uGUI Images raycast correctly (null sprite often blocks clicks).
        /// </summary>
        public static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = Texture2D.whiteTexture;
            _whiteSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            _whiteSprite.name = "WRLDZ_WhiteUI";
            return _whiteSprite;
        }

        public static void ApplySolidImage(Image img, Color color)
        {
            if (img == null) return;
            img.sprite = WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = true;
        }

        /// <summary>
        /// Ensure an EventSystem that works with the active Input System package.
        /// Project uses activeInputHandler = Input System only — StandaloneInputModule is a no-op.
        /// </summary>
        public static void EnsureEventSystem()
        {
            var existing = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            GameObject esGo;
            if (existing != null)
            {
                esGo = existing.gameObject;
            }
            else
            {
                esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
            }

            EnsureInputModule(esGo);
        }

        static void EnsureInputModule(GameObject esGo)
        {
            // Prefer new Input System module (project is Input System–only).
            var inputModType = Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");

            if (inputModType != null)
            {
                var legacy = esGo.GetComponent<StandaloneInputModule>();
                if (legacy != null)
                    UnityEngine.Object.Destroy(legacy);

                var module = esGo.GetComponent(inputModType);
                if (module == null)
                    module = esGo.AddComponent(inputModType);

                // Without default actions, pointer/click events never fire.
                try
                {
                    var actionsProp = inputModType.GetProperty(
                        "actionsAsset",
                        BindingFlags.Instance | BindingFlags.Public);
                    var actions = actionsProp?.GetValue(module);
                    if (actions == null)
                    {
                        var assign = inputModType.GetMethod(
                            "AssignDefaultActions",
                            BindingFlags.Instance | BindingFlags.Public,
                            null,
                            Type.EmptyTypes,
                            null);
                        assign?.Invoke(module, null);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] AssignDefaultActions failed: " + ex.Message);
                }

                return;
            }

            // Fallback for old Input Manager only.
            if (esGo.GetComponent<StandaloneInputModule>() == null &&
                esGo.GetComponent<BaseInputModule>() == null)
            {
                esGo.AddComponent<StandaloneInputModule>();
            }
        }
    }
}
